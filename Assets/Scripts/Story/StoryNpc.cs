using System;
using UnityEngine;
using CreatureExperiment.DailyLife;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Story
{
    /// <summary>
    /// A story character (Mike) - not an AI. It stands where it is put, can be talked to when the story says so, and
    /// walks a fixed route when told to.
    ///
    /// Talk: an <see cref="IUsable"/> target ("E · 대화하기") only while <see cref="SetTalkable"/>(true) - the story's
    /// "talk ready"; a press raises <see cref="Talked"/> and the story script runs the conversation. Otherwise E on it
    /// does nothing special. The NPC never starts a conversation by itself.
    ///
    /// Walk (<see cref="WalkRoute"/>): straight lines between the <see cref="StoryWaypoint"/> children of the route, kept on
    /// the ground with a downward ray (floors only - never snaps up onto a wall or barrier). A soft escort, never a lock:
    /// - The player counts as <b>ahead</b> when, projected onto the rest of the route, they are beyond the NPC and within
    ///   <see cref="aheadMaxOffRoute"/> of it (a route that turns or crosses a street is followed, not cut short).
    /// - Not ahead and more than <see cref="waitDistance"/> away (fallen behind, or off to the side): it stops and faces
    ///   them until they are within <see cref="resumeDistance"/> - or go on ahead.
    /// - Ahead by more than <see cref="catchUpDistance"/>: it walks at <see cref="catchUpSpeed"/> instead of
    ///   <see cref="walkSpeed"/> until the gap closes. A player ahead is never waited for and never blocked.
    /// - It holds while the player stands right in its way. Doors on the route are opened / closed by the waypoints.
    ///
    /// Guiding (<see cref="SetGuiding"/>, on while the story wants the player with the NPC - following it, or meeting it
    /// where it waits): when the player is more than <see cref="recallDistance"/> away and not simply ahead on the route,
    /// <see cref="Recalled"/> is raised once (the story shows "이쪽이야."); it re-arms only after the player comes back
    /// within <see cref="recallRearmDistance"/>.
    /// While standing it turns to face <see cref="FaceTowards"/>' point, if one was given.
    /// </summary>
    public class StoryNpc : MonoBehaviour, IUsable, IFocusTarget
    {
        [SerializeField] private string displayName = "Mike";
        [Tooltip("Head point the player's view turns to in a conversation.")]
        [SerializeField] private Transform lookTarget;
        [SerializeField] private string talkPrompt = "E · 대화하기";

        [Header("Route")]
        [Tooltip("Parent whose children (StoryWaypoint) are the route, in order.")]
        [SerializeField] private Transform route;
        [Tooltip("The player; found by tag if empty.")]
        [SerializeField] private Transform player;
        [SerializeField] private float walkSpeed = 3.6f;
        [Tooltip("Speed while the player is well ahead on the route (a brisk walk, not a run).")]
        [SerializeField] private float catchUpSpeed = 4.7f;
        [Tooltip("Player ahead on the route by more than this: catch-up speed.")]
        [SerializeField] private float catchUpDistance = 8f;
        [Tooltip("Counts as ahead only within this distance of the rest of the route (further off = wandered away).")]
        [SerializeField] private float aheadMaxOffRoute = 12f;
        [SerializeField] private float turnSpeed = 360f;
        [SerializeField] private float waitDistance = 9f;
        [SerializeField] private float resumeDistance = 5f;
        [Tooltip("Holds while the player stands this close in front.")]
        [SerializeField] private float personalSpace = 1.3f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Recall (while guiding)")]
        [SerializeField] private float recallDistance = 22f;
        [SerializeField] private float recallRearmDistance = 11f;

        [Header("Walk look (optional)")]
        [SerializeField] private Transform legL;
        [SerializeField] private Transform legR;
        [SerializeField] private Transform armL;
        [SerializeField] private Transform armR;
        [SerializeField] private float legSwing = 28f;
        [SerializeField] private float armSwing = 18f;
        [SerializeField] private float stepsPerMeter = 1.1f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private bool walking;
        [SerializeField] private bool arrived;
        [SerializeField] private bool waitingForPlayer;
        [SerializeField] private bool playerAhead;
        [SerializeField] private bool guiding;
        [SerializeField] private bool recallArmed = true;
        [SerializeField] private int waypointIndex;
        [SerializeField] private float speed;

        /// <summary>The player pressed E on this NPC while it was talkable.</summary>
        public event Action Talked;

        /// <summary>Guiding, and the player wandered off (once until they come back near).</summary>
        public event Action Recalled;

        private bool _talkable;
        private bool _hasFace;
        private bool _waitForPlayer = true;
        private Vector3 _facePoint;
        private float _phase;
        private float _swing;    // 0..1, how much the limbs swing (eases in / out)

        public string DisplayName => displayName;
        public Transform LookTarget => lookTarget != null ? lookTarget : transform;
        public bool IsWalking => walking;
        public bool HasArrived => arrived;
        public bool IsWaitingForPlayer => waitingForPlayer;
        public bool IsPlayerAhead => playerAhead;
        public float CurrentSpeed => speed;

        // ---- Talk ------------------------------------------------------------------------------------------

        public void SetTalkable(bool value) => _talkable = value;
        public bool IsTalkable => _talkable;
        public bool CanUse => _talkable;
        public void Use()
        {
            if (_talkable)
                Talked?.Invoke();
        }

        public string FocusName => _talkable ? talkPrompt : "";
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) { }

        /// <summary>Turn to face this point while standing (null = stop turning).</summary>
        public void FaceTowards(Vector3? point)
        {
            _hasFace = point.HasValue;
            if (point.HasValue)
                _facePoint = point.Value;
        }

        /// <summary>The story wants the player with this NPC (following it, or coming to where it waits): recall is active.</summary>
        public void SetGuiding(bool value)
        {
            guiding = value;
            recallArmed = true;
        }

        // ---- Walk ------------------------------------------------------------------------------------------

        /// <summary>Walk the default route (the first one set up on the NPC).</summary>
        public void WalkRoute() => WalkRoute(route, true);

        /// <summary>
        /// Walk <paramref name="newRoute"/> from its first point. <paramref name="waitForPlayer"/> false: never stop to wait for
        /// a player who falls behind (walking away / leaving the scene).
        /// </summary>
        public void WalkRoute(Transform newRoute, bool waitForPlayer)
        {
            if (newRoute != null)
                route = newRoute;
            _waitForPlayer = waitForPlayer;
            waitingForPlayer = false;
            if (route == null || route.childCount == 0)
            {
                arrived = true;
                return;
            }
            walking = true;
            arrived = false;
            waypointIndex = 0;
            speed = walkSpeed;
            _hasFace = false;
        }

        /// <summary>Fallback: put the NPC at the route's end at once (arrived, facing the last point's forward).</summary>
        public void SnapToRouteEnd()
        {
            if (route == null || route.childCount == 0)
                return;
            Transform last = route.GetChild(route.childCount - 1);
            transform.SetPositionAndRotation(last.position, Quaternion.Euler(0f, last.eulerAngles.y, 0f));
            walking = false;
            waitingForPlayer = false;
            arrived = true;
        }

        private void Awake()
        {
            if (player == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p != null)
                    player = p.transform;
            }
            speed = walkSpeed;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool moved = walking && Walk(dt);
            if (!walking)
            {
                playerAhead = false;
                if (_hasFace)
                    TurnTowards(_facePoint - transform.position, dt);
            }
            UpdateRecall();
            AnimateLimbs(moved, dt);
        }

        // Returns whether it moved this frame.
        private bool Walk(float dt)
        {
            Transform wp = route.GetChild(waypointIndex);
            var point = wp.GetComponent<StoryWaypoint>();
            Vector3 to = Flat(wp.position - transform.position);

            if (to.magnitude < 0.12f)
            {
                if (point != null && point.openDoor != null && !point.openDoor.IsOpen)
                {
                    if (!point.openDoor.IsMoving)
                        point.openDoor.Use();
                    TurnTowards(Flat(point.openDoor.transform.position - transform.position), dt);
                    return false;
                }
                if (point != null && point.closeDoor != null && point.closeDoor.IsOpen && !point.closeDoor.IsMoving
                    && (player == null || Vector3.Distance(player.position, point.closeDoor.transform.position) > point.keepOpenPlayerDistance))
                    point.closeDoor.Use();

                if (waypointIndex >= route.childCount - 1)
                {
                    walking = false;
                    arrived = true;
                    waitingForPlayer = false;
                    transform.rotation = Quaternion.Euler(0f, wp.eulerAngles.y, 0f);
                    return false;
                }
                waypointIndex++;
                return false;
            }

            float targetSpeed = walkSpeed;
            if (player != null)
            {
                Vector3 toPlayer = Flat(player.position - transform.position);
                float dist = toPlayer.magnitude;
                playerAhead = IsAheadOnRoute();
                if (!_waitForPlayer)
                    waitingForPlayer = false;
                else if (waitingForPlayer)
                    waitingForPlayer = !playerAhead && dist > resumeDistance; // caught up, or went on ahead
                else if (!playerAhead && dist > waitDistance)
                    waitingForPlayer = true;                                  // behind, or off to the side

                if (waitingForPlayer)
                {
                    TurnTowards(toPlayer, dt);
                    return false;
                }
                // Don't walk into the player.
                if (dist < personalSpace && Vector3.Dot(toPlayer.normalized, transform.forward) > 0.5f)
                    return false;
                if (_waitForPlayer && playerAhead && dist > catchUpDistance)
                    targetSpeed = catchUpSpeed;
            }
            speed = Mathf.MoveTowards(speed, targetSpeed, dt * 2f);

            TurnTowards(to, dt);
            Vector3 step = to.normalized * Mathf.Min(speed * dt, to.magnitude);
            Vector3 pos = transform.position + step;
            // Floors only: ignore anything steep or well above the feet (a wall top, a door barrier).
            if (Physics.Raycast(pos + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 3f, groundMask, QueryTriggerInteraction.Ignore)
                && hit.collider.transform != transform && !hit.collider.transform.IsChildOf(transform)
                && hit.normal.y > 0.6f && hit.point.y < transform.position.y + 0.4f)
                pos.y = hit.point.y;
            transform.position = pos;
            _phase += step.magnitude * stepsPerMeter * Mathf.PI;
            return true;
        }

        // The player is further along the rest of the route than the NPC: project them onto the remaining polyline
        // (NPC → next waypoint → … → end) and take the closest segment. Ahead = beyond the NPC along it and near it.
        private bool IsAheadOnRoute()
        {
            Vector3 p = Flat(player.position);
            Vector3 a = Flat(transform.position);
            float along = 0f, bestLateral = float.MaxValue, bestAlong = 0f;
            for (int i = waypointIndex; i < route.childCount; i++)
            {
                Vector3 b = Flat(route.GetChild(i).position);
                Vector3 seg = b - a;
                float len = seg.magnitude;
                float t = len > 0.001f ? Mathf.Clamp(Vector3.Dot(p - a, seg / len), 0f, len) : 0f;
                float lateral = (a + (len > 0.001f ? seg / len : Vector3.zero) * t - p).magnitude;
                if (lateral < bestLateral)
                {
                    bestLateral = lateral;
                    bestAlong = along + t;
                }
                along += len;
                a = b;
            }
            return bestAlong > 2f && bestLateral < aheadMaxOffRoute;
        }

        private void UpdateRecall()
        {
            if (!guiding || player == null)
                return;
            float dist = Flat(player.position - transform.position).magnitude;
            if (!recallArmed)
            {
                if (dist < recallRearmDistance)
                    recallArmed = true;
                return;
            }
            if (dist > recallDistance && !(walking && playerAhead))
            {
                recallArmed = false;
                Recalled?.Invoke();
            }
        }

        private void TurnTowards(Vector3 dir, float dt)
        {
            dir = Flat(dir);
            if (dir.sqrMagnitude < 0.0001f)
                return;
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        private void AnimateLimbs(bool moving, float dt)
        {
            _swing = Mathf.MoveTowards(_swing, moving ? 1f : 0f, dt * 4f);
            if (!moving)
                _phase = Mathf.MoveTowards(_phase, Mathf.Round(_phase / Mathf.PI) * Mathf.PI, dt * 4f);
            float s = Mathf.Sin(_phase) * _swing;
            SetSwing(legL, s * legSwing);
            SetSwing(legR, -s * legSwing);
            SetSwing(armL, -s * armSwing);
            SetSwing(armR, s * armSwing);
        }

        private static void SetSwing(Transform t, float angle)
        {
            if (t != null)
                t.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
