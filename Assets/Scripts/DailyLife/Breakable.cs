using System;
using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A solid item that shatters when struck hard enough - a plate, a bowl, a cup (later: any glass / ceramic prop).
    /// Its presence IS the "breakable" property; an item without it never breaks this way.
    ///
    /// What counts (judged in its own OnCollisionEnter, nothing global):
    /// - its own throw (F / Right Click): the first impact of that throw is judged once
    ///   (<see cref="Interactable.ClaimThrowOutcome"/>) by how fast it was flying (<see cref="Interactable.PreImpactSpeed"/>);
    /// - <see cref="Trigger.AnyImpact"/>: every other hit too, by the impact speed along the contact normal - a fall from
    ///   high enough, being knocked off a shelf by the creature. A Place or a short drop stays far below it;
    /// - <see cref="breakByHardHit"/>: hit by a thrown Hard / Sharp item (<see cref="ThrownImpact"/>), whatever its speed.
    /// A Soft item (food, a bread bag, a cigarette) running into it never counts: food thrown onto a plate lands on it.
    ///
    /// Breaking: the item is gone (or replaced by <see cref="replaceWith"/>), a handful of shards fly and settle (visual
    /// only - no colliders, no Rigidbodies, gone after <see cref="shardLifetime"/>), a loud placeholder sound plays
    /// (<see cref="ProceduralSfx"/>) and <see cref="Broke"/> is raised with <see cref="noise"/> - the hook for a future
    /// creature hearing stimulus ("something smashed over there"); nothing listens yet. Anything riding it (food on a
    /// plate) is let go and falls.
    ///
    /// Deliberately NOT used for the raw egg (<see cref="RawEggBreak"/>), the eggs in a carton (<see cref="ThrowBreakContents"/>),
    /// a work item (<see cref="WorkItem"/>, a scored state) or the beer bottle (<see cref="DrinkContainer"/> glass) - those mean
    /// something else when they break.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class Breakable : MonoBehaviour
    {
        public enum Trigger
        {
            /// <summary>Only the first impact of a player throw (and hard hits, if enabled).</summary>
            OwnThrowOnly,
            /// <summary>Any impact fast enough - throws, falls, knocks.</summary>
            AnyImpact
        }

        /// <summary>Raised as something breaks (the object is destroyed right after): what, where, how loud (0..1). Nothing listens yet.</summary>
        public static event Action<Breakable, Vector3, float> Broke;

        [Header("When it breaks")]
        [Tooltip("m/s. Own throw: flying speed at the first impact (F throw = 8, Right Click = 16). Other impacts: speed along the contact normal (a 1 m fall ≈ 4.4).")]
        [SerializeField] private float breakSpeed = 6.5f;
        [SerializeField] private Trigger breakOn = Trigger.AnyImpact;
        [Tooltip("A thrown Hard / Sharp item hitting it breaks it whatever the speed.")]
        [SerializeField] private bool breakByHardHit = true;
        [Tooltip("How long after its own throw the first impact still counts as the throw's.")]
        [SerializeField] private float maxThrowAge = 3f;

        [Header("Shards (visual only)")]
        [SerializeField] private int shardCount = 9;
        [SerializeField] private float shardSize = 0.035f;
        [SerializeField] private float shardLifetime = 2.5f;
        [Tooltip("Shard material - empty = the item's own first renderer material.")]
        [SerializeField] private Material shardMaterial;

        [Header("Sound / noise")]
        [SerializeField] private SfxKind sound = SfxKind.CeramicBreak;
        [Range(0f, 1f)]
        [SerializeField] private float volume = 1f;
        [Tooltip("How loud this is for the world (0..1) - data for a future creature hearing stimulus.")]
        [Range(0f, 1f)]
        [SerializeField] private float noise = 0.9f;

        [Header("Result")]
        [Tooltip("Optional inactive object put where it broke (broken pieces that stay). Empty = simply gone.")]
        [SerializeField] private GameObject replaceWith;

        private Interactable _item;
        private bool _broken;

        public float Noise => noise;
        public bool IsBroken => _broken;
        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private void OnCollisionEnter(Collision collision)
        {
            if (_broken || Item.IsHeld || collision.collider is CharacterController)
                return;

            Interactable other = collision.rigidbody != null ? collision.rigidbody.GetComponent<Interactable>() : null;
            if (other != null && !ThrownImpact.IsHard(other))
                return; // soft things (food onto a plate) never break it - and don't use up its throw outcome

            ContactPoint contact = collision.GetContact(0);
            if (breakByHardHit && ThrownImpact.IsFragileBreakingProjectile(other))
            {
                Break(contact.point, contact.normal);
                return;
            }

            // Its own throw: the first impact is judged once, by how fast it was flying.
            if (Item.LastThrowMode != ThrowMode.None && Item.ClaimThrowOutcome(maxThrowAge))
            {
                if (Item.PreImpactSpeed >= breakSpeed)
                    Break(contact.point, contact.normal);
                return;
            }

            if (breakOn != Trigger.AnyImpact)
                return;
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, Item.PreImpactVelocity) > 0f)
                normal = -normal;
            if (Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal)) >= breakSpeed)
                Break(contact.point, normal);
        }

        /// <summary>Shatter now (also for a future story / debug call).</summary>
        public void Break(Vector3 point, Vector3 normal)
        {
            if (_broken)
                return;
            _broken = true;

            ReleaseRiders();
            ShardFx.Spawn(transform, point, ResolveShardMaterial(), shardCount, shardSize, shardLifetime);
            ProceduralSfx.Play(sound, point, volume);
            Broke?.Invoke(this, point, noise);

            if (replaceWith != null)
            {
                GameObject rest = Instantiate(replaceWith, transform.position, transform.rotation);
                rest.SetActive(true);
            }
            Destroy(gameObject);
        }

        // Food on a plate (parented, kinematic) must not vanish with it: let it go so it falls.
        private void ReleaseRiders()
        {
            var riders = new List<Interactable>();
            foreach (var other in GetComponentsInChildren<Interactable>(true))
                if (other != Item)
                    riders.Add(other);
            foreach (var rider in riders)
            {
                if (rider.IsHeld)
                    continue;
                rider.ClearPickupLock(rider.PickupLock); // no dish left to belong to
                rider.transform.SetParent(null, worldPositionStays: true);
                foreach (var col in rider.GetComponentsInChildren<Collider>(true))
                    col.enabled = true;
                rider.Body.isKinematic = false;
                rider.Body.WakeUp();
            }
        }

        private Material ResolveShardMaterial()
        {
            if (shardMaterial != null)
                return shardMaterial;
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
                if (r.enabled && r.sharedMaterial != null && r.gameObject.name.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) < 0)
                    return r.sharedMaterial;
            return null;
        }
    }
}
