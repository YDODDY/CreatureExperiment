using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.DailyLife;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;
using CreatureExperiment.UI;

namespace CreatureExperiment.Story
{
    public enum Day1Beat
    {
        NotStarted,
        // Morning
        WakeNarration,
        Breakfast,
        BreakfastNarration,
        MeetMike,
        MikeConversation,
        FollowMike,
        WorkplaceConversation,
        GoToSecondFloor,   // clock in at the 2F reader
        EnterWorkArea,     // clocked in - walk into the work area
        WorkNarration,
        MorningWork,
        MorningWorkNarration,
        // Lunch
        MeetMikeForLunch,
        LunchConversation,
        GoToConvenienceStore,
        LunchShopping,     // inside the Convenience Store: buy food (exit held until then)
        LeaveConvenienceStore,
        ReturnToWork,
        ReturnConversation,
        // Afternoon
        AfternoonWork,
        AfternoonWorkNarration,
        MeetMikeAfterWork,
        AfterWorkConversation,
        GoToSmokingArea,
        Smoking,           // light a cigarette (Mike offers a pack if the player has none)
        SmokingConversation,
        // Evening
        GoHome,
        HomeNarration,
        Dinner,
        GoToComputer,
        PlayGame,
        GameEndNarration,
        Sleep,
        Day1Complete
    }

    public enum StorySpeaker { Narration, Player, Mike }

    [Serializable]
    public struct StoryLine
    {
        public StorySpeaker speaker;
        [TextArea(1, 3)] public string text;
        public StoryLine(StorySpeaker speaker, string text) { this.speaker = speaker; this.text = text; }
    }

    /// <summary>
    /// Day 1, wake-up to bed. One coroutine runs the beats in order; every wait listens only while its own beat is
    /// current, so eating twice, re-entering a zone, talking again or paying twice never replays a beat. Only the
    /// "buy something to eat first" warning at the store door is meant to repeat.
    ///
    /// What it reuses: <see cref="MainTaskHUD"/> (main task), <see cref="DialogueUI"/> + <see cref="PlayerControlLock"/>
    /// (lines), <see cref="ConsumeEvents"/> (breakfast = any eat / drink, dinner = food only), <see cref="CheckoutCounter.Paid"/>
    /// + <see cref="FoodClassification"/> (lunch = food actually paid at the Convenience Store), <see cref="StoreExitGate.SetStoryHold"/>
    /// (no leaving without it), <see cref="WorkplaceAttendance"/> (clock in / out), <see cref="WorkShiftController"/>
    /// (morning quota, then <see cref="WorkShiftController.StartNextSegment"/> for the afternoon), <see cref="SupervisorExitWarning"/>
    /// (held back until the afternoon is done), <see cref="Cigarette.Lit"/> (the player's own cigarette), <see cref="ComputerStation.Entered"/>
    /// / <see cref="ComputerScreen.GameEnded"/>, <see cref="DailyLifeDirector"/> (bed held back until the last beat; the
    /// day rollover ends Day 1), <see cref="StoryNpc"/> + <see cref="StoryNpcSmoking"/> (Mike) and <see cref="StoryZone"/>s.
    /// While it runs the director's phase line is hidden (shift progress + notices stay); Day 1's end gives it back.
    ///
    /// Mike never starts a story conversation by himself: when he and the player are both where the beat needs them, he
    /// becomes talk-ready ("E · 대화하기") and the conversation starts on the player's E. Only the player's own narration
    /// starts by itself (and Mike's short "buy something first" call at the store door). While the story wants the player
    /// with Mike, he soft-escorts (<see cref="StoryNpc"/>: waits, catches up, calls "이쪽이야." once if they wander off).
    ///
    /// Story lines advance with Space (Jump) only, after <see cref="minLineSeconds"/>; E starts a talk (Mike).
    /// </summary>
    public class Day1StoryController : MonoBehaviour
    {
        [Header("References (found in the scene if empty)")]
        [SerializeField] private DialogueUI dialogue;
        [SerializeField] private DailyLifeDirector director;
        [SerializeField] private WorkplaceAttendance attendance;
        [SerializeField] private WorkShiftController workShift;
        [SerializeField] private SupervisorExitWarning supervisorWarning;
        [SerializeField] private PlayerMovement player;

        [Header("Mike")]
        [SerializeField] private StoryNpc mike;
        [SerializeField] private StoryNpcSmoking mikeSmoking;
        [Tooltip("Workplace 1F → Convenience Store door (outside).")]
        [SerializeField] private Transform lunchRoute;
        [Tooltip("Inside the Convenience Store (beer corner) → just outside its door.")]
        [SerializeField] private Transform storeExitRoute;
        [Tooltip("Convenience Store door → Workplace 1F spot.")]
        [SerializeField] private Transform returnRoute;
        [Tooltip("Workplace 1F spot → back door → smoking area.")]
        [SerializeField] private Transform smokingRoute;
        [Tooltip("Smoking area → away from the Villa (Mike goes home).")]
        [SerializeField] private Transform exitRoute;
        [Tooltip("Mike is switched off once this far from the player on his way out (or at the end of the exit route).")]
        [SerializeField] private float mikeLeaveDistance = 40f;
        [Tooltip("Shown (notice line, no lock) once when the player wanders off while Mike is guiding.")]
        [SerializeField] private string mikeRecallLine = "Mike: 야, 어디 가? 이쪽이야.";
        [Tooltip("Mike waiting outside the store: walking this far away from him starts the walk back without the short talk.")]
        [SerializeField] private float storeLeaveWithoutTalkDistance = 10f;
        [Tooltip("Talk-ready when Mike has arrived and the player is in the beat's zone - or this close to Mike.")]
        [SerializeField] private float talkReadyDistance = 4f;

        [Header("Zones")]
        [Tooltip("Inside the Workplace front door, past the door's swing.")]
        [SerializeField] private StoryZone workplaceZone;
        [Tooltip("Inside the 2F work area, past the Work Door (arrival only - completes the clock-in task).")]
        [SerializeField] private StoryZone workAreaZone;
        [Tooltip("A few steps further into the work area: the work-start narration plays here.")]
        [SerializeField] private StoryZone workAreaNarrationZone;
        [Tooltip("Inside the Convenience Store, past the door's swing.")]
        [SerializeField] private StoryZone storeInsideZone;
        [Tooltip("The sidewalk just outside the Convenience Store door.")]
        [SerializeField] private StoryZone storeOutsideZone;
        [SerializeField] private StoryZone smokingZone;
        [Tooltip("Just inside the player's own front door (home 202) (arrival only - completes the go-home task).")]
        [SerializeField] private StoryZone homeZone;
        [Tooltip("A few steps further into the home: the home narration plays here.")]
        [SerializeField] private StoryZone homeNarrationZone;
        [Tooltip("The player must stay this long in a narration zone before its narration starts (leaving resets it).")]
        [SerializeField] private float narrationDelay = 0.4f;
        [Tooltip("Fallback: after arriving, the narration also starts once the player has spent this long in the arrival / narration zones.")]
        [SerializeField] private float narrationFallbackSeconds = 5.5f;

        [Header("Lunch")]
        [SerializeField] private StoreExitGate storeExitGate;
        [SerializeField] private string convenienceStoreId = "ConvenienceStore";

        [Header("Smoking")]
        [Tooltip("Inactive store cigarette pack cloned as Mike's own pack (its StoreProduct is removed - it is Mike's).")]
        [SerializeField] private GameObject cigarettePackTemplate;
        [SerializeField] private int offeredPackCigarettes = 6;
        [SerializeField] private string cigaretteContentId = "Cigarette";

        [Header("Flow")]
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private float startDelay = 0.6f;
        [Tooltip("Pause after a beat's condition is met before its narration (lets an eating motion / a door settle).")]
        [SerializeField] private float beatPause = 0.6f;
        [Tooltip("If the player is waiting inside the Workplace this long while Mike is still on his way, Mike is put at his spot.")]
        [SerializeField] private float mikeArrivalFallbackSeconds = 25f;
        [Tooltip("Seconds THE END stays up before the narration.")]
        [SerializeField] private float gameEndPause = 1f;

        [Header("Conversation")]
        [SerializeField] private string playerSpeakerName = "나";
        [SerializeField] private float minLineSeconds = 0.35f;
        [SerializeField] private float lookDuration = 0.6f;

        [Header("Tasks")]
        [SerializeField] private string taskBreakfast = "아침 먹기";
        [SerializeField] private string taskMeetMike = "큰길에서 Mike 만나기";
        [SerializeField] private string taskFollowMike = "Mike를 따라 직장으로 이동하기";
        [SerializeField] private string taskClockIn = "2층 작업구역으로 이동해 출근하기";
        [SerializeField] private string taskMorningWork = "오전 작업 수행하기";
        [Tooltip("Mike already waits on 1F; the beat goes on with E on him.")]
        [SerializeField] private string taskMeetMikeDownstairs = "1층에서 Mike와 대화하기";
        [Tooltip("Shown once Mike is right there and talk-ready (replaces a 'meet / follow / go back' task).")]
        [SerializeField] private string taskTalkToMike = "Mike와 대화하기";
        [Tooltip("Within this distance of a talk-ready Mike, a 'meet Mike' task turns into the talk task.")]
        [SerializeField] private float talkTaskDistance = 6f;
        [SerializeField] private string taskGoToStore = "편의점 가기";
        [SerializeField] private string taskBuyLunch = "점심식사 구입하기";
        [SerializeField] private string taskBackToWork = "일터로 돌아가기";
        [SerializeField] private string taskAfternoonWork = "오후 작업 수행하기";
        [SerializeField] private string taskFollowToSmoking = "Mike를 따라 흡연구역으로 이동하기";
        [SerializeField] private string taskSmoke = "Mike와 담배 피우기";
        [SerializeField] private string taskGoHome = "귀가하기";
        [SerializeField] private string taskDinner = "저녁식사 하기";
        [SerializeField] private string taskComputer = "침실로 이동해 컴퓨터 켜기";
        [SerializeField] private string taskPlayGame = "게임하기";
        [SerializeField] private string taskSleep = "취침하기";

        [Header("Lines - morning")]
        [SerializeField] private StoryLine[] wakeLines =
        {
            new StoryLine(StorySpeaker.Narration, "...또 아침이네."),
        };
        [SerializeField] private StoryLine[] breakfastDoneLines =
        {
            new StoryLine(StorySpeaker.Narration, "아침도 먹었으니 슬슬 출근할까."),
            new StoryLine(StorySpeaker.Narration, "오늘은 Mike랑 같이 가기로 했지."),
            new StoryLine(StorySpeaker.Narration, "밖으로 나가서 앞으로 쭉 가면 나오는\n큰길 쪽에서 기다리고 있을 거야."),
        };
        [SerializeField] private StoryLine[] mikeMeetLines =
        {
            new StoryLine(StorySpeaker.Mike, "요. 이제 나오냐? 먼저 가는 줄 알았잖아."),
            new StoryLine(StorySpeaker.Player, "미안. 어제 좀 늦게 잠들었어."),
            new StoryLine(StorySpeaker.Mike, "또 게임한 건 아니지?"),
            new StoryLine(StorySpeaker.Player, "뭐 어때. 지각만 안 하면 된 거 아니야?"),
            new StoryLine(StorySpeaker.Mike, "안 하긴 하지. 할 뻔한 건 백 번쯤 되고."),
            new StoryLine(StorySpeaker.Mike, "가자. 늦겠다."),
        };
        [SerializeField] private StoryLine[] workplaceLines =
        {
            new StoryLine(StorySpeaker.Mike, "난 오늘 1층 작업구역 담당이야. 너는?"),
            new StoryLine(StorySpeaker.Player, "난 늘 2층이지.\n당분간은 쭉 2층 작업구역에 갈 것 같아."),
            new StoryLine(StorySpeaker.Mike, "뭐, 빡세진 않으니까 다행이지.\n그럼 서둘러 가 봐. 오늘도 적당히 일해보자고."),
            new StoryLine(StorySpeaker.Player, "그래. 이따가 점심때 보자."),
        };
        [SerializeField] private StoryLine[] workStartLines =
        {
            new StoryLine(StorySpeaker.Narration, "벌써부터 집에 가고 싶다."),
            new StoryLine(StorySpeaker.Narration, "하지만 일을 해야 하고 싶은 게임 CD도 사고,\n방세도 내고 하는 거니까..."),
            new StoryLine(StorySpeaker.Narration, "더 미루지 말고 일을 시작하자."),
        };
        [SerializeField] private StoryLine[] morningDoneLines =
        {
            new StoryLine(StorySpeaker.Narration, "휴, 오전 작업은 여기까지 하고 쉬어야겠어.\n배가 고픈걸."),
            new StoryLine(StorySpeaker.Narration, "그러고 보니 곧 점심시간이야."),
            new StoryLine(StorySpeaker.Narration, "Mike가 1층에서 기다리고 있을 테니\n작업을 마무리하고 나가보자."),
        };

        [Header("Lines - lunch")]
        [SerializeField] private StoryLine[] lunchLines =
        {
            new StoryLine(StorySpeaker.Mike, "다행히 오전은 후딱 지나갔네.\n배고프다. 뭐 먹을 거야?"),
            new StoryLine(StorySpeaker.Player, "멀리 가기도 귀찮은데.\n그냥 편의점이나 가자."),
            new StoryLine(StorySpeaker.Mike, "그러지 뭐.\n나도 대충 뭐 좀 사 먹어야겠다."),
            new StoryLine(StorySpeaker.Player, "가자."),
        };
        [SerializeField] private StoryLine[] noFoodExitLines =
        {
            new StoryLine(StorySpeaker.Mike, "야, 아무것도 안 사?\n오후에 일하다 쓰러진다."),
        };
        [SerializeField] private StoryLine[] ateInStoreLines =
        {
            new StoryLine(StorySpeaker.Mike, "어지간히 배고팠나 보네."),
        };
        [SerializeField] private StoryLine[] leaveStoreLines =
        {
            new StoryLine(StorySpeaker.Mike, "일터로 돌아가서 밥 먹고 좀 쉬자고."),
        };
        [SerializeField] private StoryLine[] returnLines =
        {
            new StoryLine(StorySpeaker.Mike, "그럼 오후 일과도 힘내보자고."),
            new StoryLine(StorySpeaker.Player, "그래. 일 다 끝나고 보자."),
        };

        [Header("Lines - afternoon / smoking")]
        [SerializeField] private StoryLine[] afternoonDoneLines =
        {
            new StoryLine(StorySpeaker.Narration, "드디어 다 끝냈다."),
            new StoryLine(StorySpeaker.Narration, "빠르게 마무리하고 퇴근하자.\n1층에서 Mike가 기다리고 있을 거야."),
        };
        [Tooltip("Talking to Mike after work before clocking out (repeatable).")]
        [SerializeField] private StoryLine[] notClockedOutLines =
        {
            new StoryLine(StorySpeaker.Mike, "퇴근 카드부터 찍고 와."),
        };
        [SerializeField] private StoryLine[] afterWorkLines =
        {
            new StoryLine(StorySpeaker.Mike, "오늘도 수고 많았어.\n잠시 담배나 한 대 피우고 가려는데, 같이 갈래?"),
            new StoryLine(StorySpeaker.Player, "그래."),
        };
        [SerializeField] private StoryLine[] offerPackLines =
        {
            new StoryLine(StorySpeaker.Mike, "뭐야. 담배 없어?\n내 거 빌려줄게."),
        };
        [Tooltip("Talking to Mike at the smoking area while the player already has a cigarette.")]
        [SerializeField] private StoryLine[] lightUpLines =
        {
            new StoryLine(StorySpeaker.Mike, "자, 한 대 피우자."),
        };
        [SerializeField] private StoryLine[] smokingLines =
        {
            new StoryLine(StorySpeaker.Mike, "내일은 좀 든든한 거 사 먹자.\n대충 편의점 음식으로 때우니까 배고파 죽겠어."),
            new StoryLine(StorySpeaker.Player, "글쎄.\n내일 퇴근하면서 새로운 게임 CD를 사야 해서 돈이 될지 모르겠네."),
            new StoryLine(StorySpeaker.Mike, "너 게임 CD 산 지 얼마 안 되지 않았어?\n벌써 또 새로운 걸 사는 거야?"),
            new StoryLine(StorySpeaker.Player, "응. 아마 오늘 집에 가서 엔딩을 볼 것 같아.\n그럼 또 새로운 걸 해야지."),
            new StoryLine(StorySpeaker.Mike, "밥 굶고 게임을 하겠다니.\n이해할 수가 없네."),
            new StoryLine(StorySpeaker.Player, "굶진 않지.\n그러는 너는 술담배에 돈 다 꼬라박잖아."),
            new StoryLine(StorySpeaker.Mike, "난 삶의 낙이 술이야."),
            new StoryLine(StorySpeaker.Player, "어련하시겠어."),
            new StoryLine(StorySpeaker.Mike, "말 나온 김에 언제 시간 되면 퇴근하고 맥주나 한잔하자고.\n최근에 새로 연 펍이 하나 있는데 분위기가 꽤 괜찮아."),
            new StoryLine(StorySpeaker.Mike, "안주도 맛있고."),
            new StoryLine(StorySpeaker.Player, "그래. 그러지 뭐."),
            new StoryLine(StorySpeaker.Mike, "그럼 내일 또 보자고.\n조심히 들어가라."),
            new StoryLine(StorySpeaker.Player, "너도 잘 들어가라."),
        };

        [Header("Lines - evening")]
        [SerializeField] private StoryLine[] homeLines =
        {
            new StoryLine(StorySpeaker.Narration, "오늘도 지루한 하루가 끝나가는군."),
            new StoryLine(StorySpeaker.Narration, "간단하게 저녁을 먹고,\n하던 게임이나 해볼까?"),
        };
        [SerializeField] private StoryLine[] gameEndLines =
        {
            new StoryLine(StorySpeaker.Narration, "드디어 엔딩을 봤네.\n나름 재밌었어."),
            new StoryLine(StorySpeaker.Narration, "시간이 벌써 이렇게 됐다니.\n이제 자러 가자."),
        };

        [Header("State (read-only, for debugging)")]
        [SerializeField] private Day1Beat beat = Day1Beat.NotStarted;
        [SerializeField] private bool lunchFoodPurchased;
        [SerializeField] private bool lunchFoodEatenInStore;

        private InputAction _space;
        private bool _consumed;
        private bool _mikeTalked;
        private bool _playerLit;
        private bool _computerEntered;
        private bool _gameEnded;
        private bool _dayStarted;
        private bool _inConversation;
        private PlayerControlLock _lock;
        private PlayerInteractor _interactor;
        private ObjectiveHUD _hud;

        public Day1Beat Beat => beat;
        public bool InConversation => _inConversation;
        public bool LunchFoodPurchased => lunchFoodPurchased;

        private void Awake()
        {
            if (dialogue == null) dialogue = FindFirstObjectByType<DialogueUI>();
            if (director == null) director = FindFirstObjectByType<DailyLifeDirector>();
            if (attendance == null) attendance = FindFirstObjectByType<WorkplaceAttendance>();
            if (workShift == null) workShift = FindFirstObjectByType<WorkShiftController>();
            if (supervisorWarning == null) supervisorWarning = FindFirstObjectByType<SupervisorExitWarning>();
            if (player == null) player = FindFirstObjectByType<PlayerMovement>();
            if (mikeSmoking == null && mike != null) mikeSmoking = mike.GetComponent<StoryNpcSmoking>();
            _interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;
            _hud = FindFirstObjectByType<ObjectiveHUD>();

            InputActionAsset asset = player != null ? player.InputActions : null;
            var map = asset != null ? asset.FindActionMap("Player", throwIfNotFound: false) : null;
            _space = map?.FindAction("Jump", throwIfNotFound: false);
        }

        private void OnEnable()
        {
            ConsumeEvents.Consumed += OnConsumed;
            CheckoutCounter.Paid += OnPaid;
            Cigarette.Lit += OnCigaretteLit;
            ComputerStation.Entered += OnComputerEntered;
            ComputerScreen.GameEnded += OnGameEnded;
            if (mike != null) mike.Talked += OnMikeTalked;
            if (mike != null) mike.Recalled += OnMikeRecalled;
            if (director != null) director.DayStarted += OnDayStarted;
        }

        private void OnDisable()
        {
            ConsumeEvents.Consumed -= OnConsumed;
            CheckoutCounter.Paid -= OnPaid;
            Cigarette.Lit -= OnCigaretteLit;
            ComputerStation.Entered -= OnComputerEntered;
            ComputerScreen.GameEnded -= OnGameEnded;
            if (mike != null) mike.Talked -= OnMikeTalked;
            if (mike != null) mike.Recalled -= OnMikeRecalled;
            if (director != null) director.DayStarted -= OnDayStarted;
            if (storeExitGate != null) storeExitGate.SetStoryHold(null, null);
            if (_inConversation)
            {
                // Never strand the player locked in a conversation.
                StopAllCoroutines();
                if (dialogue != null) dialogue.Hide();
                _lock?.Release();
                _lock = null;
                _inConversation = false;
            }
        }

        private void Start()
        {
            if (playOnStart)
                StartCoroutine(Run());
        }

        // ---- Events: each only counts in its own beat ----------------------------------------------------------

        private void OnConsumed(GameObject _, ConsumeKind kind)
        {
            if (beat == Day1Beat.Breakfast)
                _consumed = true;
            else if (beat == Day1Beat.Dinner && kind == ConsumeKind.Food)
                _consumed = true;
            else if (beat == Day1Beat.LunchShopping && kind == ConsumeKind.Food && lunchFoodPurchased)
                lunchFoodEatenInStore = true;
        }

        private void OnPaid(CheckoutCounter counter, IReadOnlyList<StoreProduct> products)
        {
            if ((beat != Day1Beat.GoToConvenienceStore && beat != Day1Beat.LunchShopping) || counter.StoreId != convenienceStoreId)
                return;
            foreach (var p in products)
                if (p != null && FoodClassification.IsFood(p.gameObject))
                {
                    lunchFoodPurchased = true;
                    Debug.Log($"[Day1Story] Lunch food paid: {p.name}.");
                    return;
                }
        }

        private void OnCigaretteLit(Cigarette cigarette)
        {
            if (beat == Day1Beat.Smoking && IsPlayers(cigarette))
                _playerLit = true;
        }

        private void OnComputerEntered(ComputerStation _)
        {
            if (beat == Day1Beat.GoToComputer)
                _computerEntered = true;
        }

        private void OnGameEnded(ComputerScreen _)
        {
            if (beat == Day1Beat.PlayGame)
                _gameEnded = true;
        }

        private void OnMikeTalked()
        {
            if (!_inConversation)
                _mikeTalked = true;
        }

        private void OnMikeRecalled()
        {
            if (!_inConversation && _hud != null && !string.IsNullOrEmpty(mikeRecallLine))
                _hud.ShowNotice(mikeRecallLine);
        }

        private void OnDayStarted(int day)
        {
            if (beat == Day1Beat.Sleep)
                _dayStarted = true;
        }

        // ---- The day ---------------------------------------------------------------------------------------------

        private IEnumerator Run()
        {
            if (director != null)
            {
                director.SetPhaseObjectiveHidden(true);
                director.SetSleepBlocked(true);
            }
            if (supervisorWarning != null)
                supervisorWarning.SetSuppressed(true); // no "clock out" warning over the lunch break
            MainTaskHUD.ClearTask();
            if (mike != null)
                mike.SetTalkable(false);
            yield return new WaitForSeconds(startDelay);

            yield return Morning();
            yield return Lunch();
            yield return Afternoon();
            yield return Evening();
        }

        private IEnumerator Morning()
        {
            beat = Day1Beat.WakeNarration;
            yield return Talk(wakeLines, null);

            // Breakfast: any real eat / drink while this beat is current.
            beat = Day1Beat.Breakfast;
            _consumed = false;
            MainTaskHUD.SetTask(taskBreakfast);
            yield return new WaitUntil(() => _consumed);
            yield return new WaitForSeconds(beatPause);
            beat = Day1Beat.BreakfastNarration;
            MainTaskHUD.CompleteTask();
            yield return Talk(breakfastDoneLines, null);

            beat = Day1Beat.MeetMike;
            MainTaskHUD.SetTask(taskMeetMike);
            yield return WaitForMikeTalk(taskTalkToMike); // "meet" while finding him, "talk" once by his side
            beat = Day1Beat.MikeConversation;
            MainTaskHUD.CompleteTask();
            yield return TalkToMike(mikeMeetLines);

            beat = Day1Beat.FollowMike;
            MainTaskHUD.SetTask(taskFollowMike);
            SetGuiding(true);
            if (mike != null)
                mike.WalkRoute();
            yield return WaitForMikeTalkAt(workplaceZone);

            beat = Day1Beat.WorkplaceConversation;
            SetGuiding(false);
            MainTaskHUD.CompleteTask();
            yield return TalkToMike(workplaceLines);

            beat = Day1Beat.GoToSecondFloor;
            MainTaskHUD.SetTask(taskClockIn);
            yield return new WaitUntil(() => attendance == null || attendance.State != AttendanceState.NotClockedIn);
            beat = Day1Beat.EnterWorkArea;
            yield return new WaitUntil(() => InZone(workAreaZone));
            MainTaskHUD.CompleteTask(); // arrived; the narration waits until the player is a few steps further in
            yield return WaitForNarrationSpot(workAreaZone, workAreaNarrationZone);
            beat = Day1Beat.WorkNarration;
            yield return Talk(workStartLines, null);

            beat = Day1Beat.MorningWork;
            MainTaskHUD.SetTask(taskMorningWork);
            yield return new WaitUntil(() => SegmentWorkDone);
            yield return new WaitForSeconds(beatPause);
            beat = Day1Beat.MorningWorkNarration;
            MainTaskHUD.CompleteTask();
            yield return Talk(morningDoneLines, null);
        }

        private IEnumerator Lunch()
        {
            beat = Day1Beat.MeetMikeForLunch;
            MainTaskHUD.SetTask(taskMeetMikeDownstairs);
            yield return WaitForMikeTalk();
            beat = Day1Beat.LunchConversation;
            MainTaskHUD.CompleteTask();
            yield return TalkToMike(lunchLines);

            // Off to the Convenience Store. No leaving it without food paid there (unpaid goods: the cashier's block comes first).
            lunchFoodPurchased = false;
            lunchFoodEatenInStore = false;
            beat = Day1Beat.GoToConvenienceStore;
            MainTaskHUD.SetTask(taskGoToStore);
            SetGuiding(true);
            if (storeExitGate != null)
                storeExitGate.SetStoryHold(
                    () => (beat == Day1Beat.GoToConvenienceStore || beat == Day1Beat.LunchShopping) && !lunchFoodPurchased,
                    () => TryStartTalk(noFoodExitLines, mike != null ? mike.LookTarget : null));
            if (mike != null)
                mike.WalkRoute(lunchRoute, true);
            yield return new WaitUntil(() => InZone(storeInsideZone));

            beat = Day1Beat.LunchShopping;
            MainTaskHUD.CompleteTask();
            MainTaskHUD.SetTask(taskBuyLunch);
            yield return new WaitUntil(() => lunchFoodPurchased && InZone(storeOutsideZone) && !_inConversation);
            if (storeExitGate != null)
                storeExitGate.SetStoryHold(null, null);

            // Out with food: back to work. Mike comes out too and is talk-ready by the door; the short line is optional -
            // walking off without it starts the walk back anyway (never stuck waiting for a talk nobody needs).
            beat = Day1Beat.LeaveConvenienceStore;
            MainTaskHUD.CompleteTask();
            MainTaskHUD.SetTask(taskBackToWork);
            if (mike != null)
            {
                mike.WalkRoute(storeExitRoute, true);
                yield return new WaitUntil(() => mike.HasArrived);
                _mikeTalked = false;
                mike.SetTalkable(true);
                yield return new WaitUntil(() => _mikeTalked
                    || Vector3.Distance(PlayerPosition, mike.transform.position) > storeLeaveWithoutTalkDistance);
                mike.SetTalkable(false);
                if (_mikeTalked)
                    yield return TalkToMike(lunchFoodEatenInStore ? Concat(ateInStoreLines, leaveStoreLines) : leaveStoreLines);
            }

            beat = Day1Beat.ReturnToWork;
            if (mike != null)
                mike.WalkRoute(returnRoute, true);
            yield return WaitForMikeTalkAt(workplaceZone);

            beat = Day1Beat.ReturnConversation;
            SetGuiding(false);
            MainTaskHUD.CompleteTask();
            yield return TalkToMike(returnLines);
        }

        private IEnumerator Afternoon()
        {
            // Same shift, still clocked in: a fresh batch for the afternoon.
            if (workShift != null)
                workShift.StartNextSegment();
            beat = Day1Beat.AfternoonWork;
            MainTaskHUD.SetTask(taskAfternoonWork);
            yield return new WaitUntil(() => SegmentWorkDone);
            yield return new WaitForSeconds(beatPause);
            beat = Day1Beat.AfternoonWorkNarration;
            MainTaskHUD.CompleteTask();
            yield return Talk(afternoonDoneLines, null);

            // From here the usual "clock out before you go" warning applies again.
            if (supervisorWarning != null)
                supervisorWarning.SetSuppressed(false);
            beat = Day1Beat.MeetMikeAfterWork;
            MainTaskHUD.SetTask(taskMeetMikeDownstairs);
            while (true)
            {
                yield return WaitForMikeTalk();
                if (attendance == null || attendance.State == AttendanceState.ClockedOut)
                    break;
                yield return TalkToMike(notClockedOutLines); // talk again after clocking out
            }
            beat = Day1Beat.AfterWorkConversation;
            MainTaskHUD.CompleteTask();
            yield return TalkToMike(afterWorkLines);

            beat = Day1Beat.GoToSmokingArea;
            MainTaskHUD.SetTask(taskFollowToSmoking);
            SetGuiding(true);
            if (mike != null)
                mike.WalkRoute(smokingRoute, true);
            yield return WaitForMikeAndPlayerIn(smokingZone);

            // Smoking: Mike smokes and is talk-ready. E before the player's own cigarette is lit: he lends a pack (no
            // cigarette at all) or says "light up". Once it is lit, the next E starts the long talk - lighting it never does.
            beat = Day1Beat.Smoking;
            if (mikeSmoking != null)
                mikeSmoking.StartSmoking();
            MainTaskHUD.CompleteTask();
            MainTaskHUD.SetTask(taskSmoke);
            _playerLit = PlayerHoldsLitCigarette(); // already smoking one: that counts
            _mikeTalked = false;
            bool offered = false;
            if (mike != null)
                mike.SetTalkable(true);
            while (true)
            {
                if (_mikeTalked && !_inConversation)
                {
                    _mikeTalked = false;
                    if (_playerLit)
                        break;
                    if (!offered && !PlayerHasCigarette())
                    {
                        yield return TalkToMike(offerPackLines);
                        OfferPack();
                        offered = true;
                    }
                    else
                    {
                        yield return TalkToMike(lightUpLines);
                    }
                    continue;
                }
                yield return null;
            }
            if (mike != null)
                mike.SetTalkable(false);

            beat = Day1Beat.SmokingConversation;
            SetGuiding(false);
            MainTaskHUD.CompleteTask();
            yield return TalkToMike(smokingLines);
        }

        private IEnumerator Evening()
        {
            beat = Day1Beat.GoHome;
            MainTaskHUD.SetTask(taskGoHome);
            if (mike != null)
            {
                if (mikeSmoking != null)
                {
                    mikeSmoking.StopSmoking();
                    mikeSmoking.ClearOffer();
                }
                mike.FaceTowards(null);
                mike.SetGuiding(false);
                mike.WalkRoute(exitRoute, false);
                StartCoroutine(MikeLeaves());
            }
            yield return new WaitUntil(() => InZone(homeZone));
            MainTaskHUD.CompleteTask(); // arrived; time to close the door / switch the light on before the narration
            yield return WaitForNarrationSpot(homeZone, homeNarrationZone);

            beat = Day1Beat.HomeNarration;
            yield return Talk(homeLines, null);

            beat = Day1Beat.Dinner;
            _consumed = false;
            MainTaskHUD.SetTask(taskDinner);
            yield return new WaitUntil(() => _consumed); // food only (drinks / beer / cigarettes don't count)
            yield return new WaitForSeconds(beatPause);
            MainTaskHUD.CompleteTask();

            beat = Day1Beat.GoToComputer;
            _computerEntered = ComputerInUse();
            MainTaskHUD.SetTask(taskComputer);
            yield return new WaitUntil(() => _computerEntered);

            beat = Day1Beat.PlayGame;
            _gameEnded = false;
            MainTaskHUD.CompleteTask();
            MainTaskHUD.SetTask(taskPlayGame);
            yield return new WaitUntil(() => _gameEnded);
            yield return new WaitForSeconds(gameEndPause);

            beat = Day1Beat.GameEndNarration;
            MainTaskHUD.CompleteTask();
            yield return Talk(gameEndLines, null);

            beat = Day1Beat.Sleep;
            _dayStarted = false;
            MainTaskHUD.SetTask(taskSleep);
            if (director != null)
                director.SetSleepBlocked(false);
            yield return new WaitUntil(() => _dayStarted);

            // Day 1 is over: hand the screen back to the ordinary daily loop.
            beat = Day1Beat.Day1Complete;
            MainTaskHUD.ClearTask();
            if (director != null)
                director.SetPhaseObjectiveHidden(false);
            if (mike != null)
            {
                mike.SetTalkable(false);
                mike.gameObject.SetActive(false);
            }
            Debug.Log("[Day1Story] Day 1 complete.");
        }

        // ---- Waits -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Mike is talk-ready; the beat goes on with the player's E. <paramref name="nearTask"/>: the task line switches to it
        /// once the player is within <see cref="talkTaskDistance"/> of him (found him - now talk). Null = the task stays.
        /// </summary>
        private IEnumerator WaitForMikeTalk(string nearTask = null)
        {
            if (mike == null)
                yield break;
            _mikeTalked = false;
            mike.SetTalkable(true);
            bool switched = string.IsNullOrEmpty(nearTask);
            while (!_mikeTalked)
            {
                if (!switched && Vector3.Distance(PlayerPosition, mike.transform.position) < talkTaskDistance)
                {
                    MainTaskHUD.SetTask(nearTask);
                    switched = true;
                }
                yield return null;
            }
            mike.SetTalkable(false);
        }

        /// <summary>
        /// Both must be there: Mike at his route's end and the player in <paramref name="zone"/> (or right by Mike). Then Mike
        /// is talk-ready and the beat goes on with the player's E. Arriving first - either of them - just waits.
        /// </summary>
        private IEnumerator WaitForMikeTalkAt(StoryZone zone)
        {
            if (mike == null)
                yield break;
            yield return WaitForMikeAndPlayerIn(zone);
            _mikeTalked = false;
            mike.SetTalkable(true);
            MainTaskHUD.SetTask(taskTalkToMike); // both arrived: following / going back is done, the talk is next
            yield return new WaitUntil(() => _mikeTalked);
            mike.SetTalkable(false);
        }

        private bool PlayerNearMike => mike != null && Vector3.Distance(PlayerPosition, mike.transform.position) < talkReadyDistance;

        private void SetGuiding(bool value)
        {
            if (mike != null)
                mike.SetGuiding(value);
        }

        private IEnumerator WaitForMikeAndPlayerIn(StoryZone zone)
        {
            float waitingInside = 0f;
            while (!((mike == null || mike.HasArrived) && (InZone(zone) || PlayerNearMike)))
            {
                waitingInside = InZone(zone) ? waitingInside + Time.deltaTime : 0f;
                if (mike != null && !mike.HasArrived && waitingInside > mikeArrivalFallbackSeconds)
                    mike.SnapToRouteEnd();
                yield return null;
            }
        }

        private IEnumerator MikeLeaves()
        {
            while (mike != null && mike.gameObject.activeSelf)
            {
                float dist = Vector3.Distance(mike.transform.position, PlayerPosition);
                if (mike.HasArrived || dist > mikeLeaveDistance)
                {
                    mike.gameObject.SetActive(false);
                    yield break;
                }
                yield return null;
            }
        }

        private bool SegmentWorkDone =>
            workShift != null && workShift.BoxOpened && workShift.Current.resolvedCount >= workShift.DailyQuota;

        private Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;

        private bool InZone(StoryZone zone) => zone == null || zone.Contains(PlayerPosition);

        /// <summary>
        /// After an arrival: waits until the player has stood <see cref="narrationDelay"/> inside the narration zone
        /// (stepping out resets it), or has lingered <see cref="narrationFallbackSeconds"/> around the arrival so standing
        /// by the door never stalls the story. Door / light state is never a condition.
        /// </summary>
        private IEnumerator WaitForNarrationSpot(StoryZone arrival, StoryZone narration)
        {
            float dwell = 0f, lingering = 0f;
            while (true)
            {
                bool inNarration = InZone(narration);
                dwell = inNarration ? dwell + Time.deltaTime : 0f;
                if (inNarration || InZone(arrival))
                    lingering += Time.deltaTime;
                if (dwell >= narrationDelay || lingering >= narrationFallbackSeconds)
                    yield break;
                yield return null;
            }
        }

        private static bool ComputerInUse()
        {
            foreach (var c in FindObjectsByType<ComputerStation>(FindObjectsSortMode.None))
                if (c.IsActive)
                    return true;
            return false;
        }

        // ---- Cigarettes ------------------------------------------------------------------------------------------

        private bool IsPlayers(Cigarette cigarette)
        {
            if (_interactor == null || cigarette == null)
                return false;
            for (int i = 0; i < _interactor.SlotCount; i++)
                if (_interactor.GetSlotItem(i) != null && _interactor.GetSlotItem(i).gameObject == cigarette.gameObject)
                    return true;
            return false;
        }

        private bool PlayerHoldsLitCigarette()
        {
            if (_interactor == null)
                return false;
            for (int i = 0; i < _interactor.SlotCount; i++)
            {
                Interactable item = _interactor.GetSlotItem(i);
                if (item != null && item.TryGetComponent(out Cigarette c) && c.State == CigaretteState.Lit)
                    return true;
            }
            return false;
        }

        /// <summary>A cigarette that can still be smoked, or a (paid / own) pack with some left, anywhere in the inventory.</summary>
        private bool PlayerHasCigarette()
        {
            if (_interactor == null)
                return false;
            for (int i = 0; i < _interactor.SlotCount; i++)
            {
                Interactable item = _interactor.GetSlotItem(i);
                if (item == null)
                    continue;
                if (item.TryGetComponent(out Cigarette c) && c.State != CigaretteState.Spent)
                    return true;
                if (item.TryGetComponent(out ConsumableStock stock) && stock.ContentId == cigaretteContentId
                    && !stock.IsEmpty && !StoreProduct.IsUnpaidPackage(stock))
                    return true;
            }
            return false;
        }

        // Mike's own pack: a copy of the store pack without its StoreProduct (not merchandise), a few cigarettes left.
        private void OfferPack()
        {
            if (cigarettePackTemplate == null || mikeSmoking == null)
                return;
            GameObject pack = Instantiate(cigarettePackTemplate);
            pack.name = "MikeCigarettePack";
            pack.transform.localScale = cigarettePackTemplate.transform.localScale;
            if (pack.TryGetComponent(out StoreProduct product))
                DestroyImmediate(product);
            if (pack.TryGetComponent(out ConsumableStock stock))
                stock.SetCurrent(offeredPackCigarettes);
            pack.SetActive(true);
            mikeSmoking.Offer(pack.GetComponent<Interactable>());
        }

        // ---- Lines -----------------------------------------------------------------------------------------------

        private IEnumerator TalkToMike(StoryLine[] lines)
        {
            if (mike != null)
                mike.FaceTowards(PlayerPosition);
            yield return Talk(lines, mike != null ? mike.LookTarget : null);
        }

        /// <summary>Start a conversation now unless one is running (for callers outside the beat flow, e.g. the store door).</summary>
        private bool TryStartTalk(StoryLine[] lines, Transform lookAt)
        {
            if (_inConversation || dialogue == null)
                return false;
            if (mike != null)
                mike.FaceTowards(PlayerPosition);
            StartCoroutine(Talk(lines, lookAt));
            return true;
        }

        /// <summary>Lock the player, turn the view to <paramref name="lookAt"/> (if any), show the lines one by one (Space), give control back.</summary>
        private IEnumerator Talk(StoryLine[] lines, Transform lookAt)
        {
            if (dialogue == null || lines == null || lines.Length == 0)
                yield break;

            _inConversation = true;
            _lock = PlayerControlLock.Acquire(player != null ? player.gameObject : null);
            if (lookAt != null)
                yield return TurnView(lookAt);

            // The lock switched PlayerMovement off, and with it Jump (Space).
            bool spaceWasOn = _space != null && _space.enabled;
            _space?.Enable();

            foreach (StoryLine line in lines)
            {
                dialogue.Show(SpeakerName(line.speaker), line.speaker == StorySpeaker.Narration ? $"<i>{line.text}</i>" : line.text);
                float shownAt = Time.time;
                yield return null;
                while (!(Time.time - shownAt >= minLineSeconds && AdvancePressed()))
                {
                    if (_space == null && Time.time - shownAt > 3f)
                        break; // no input wired - don't trap the player
                    yield return null;
                }
            }

            dialogue.Hide();
            if (_space != null && !spaceWasOn) _space.Disable();
            // Give control back a frame later so the closing press isn't also read as a jump.
            yield return null;
            _lock.Release();
            _lock = null;
            _inConversation = false;
        }

        private bool AdvancePressed() => !PauseMenu.IsPaused && _space != null && _space.WasPressedThisFrame();

        private string SpeakerName(StorySpeaker speaker)
        {
            switch (speaker)
            {
                case StorySpeaker.Player: return playerSpeakerName;
                case StorySpeaker.Mike: return mike != null ? mike.DisplayName : "Mike";
                default: return "";
            }
        }

        private static StoryLine[] Concat(StoryLine[] a, StoryLine[] b)
        {
            var all = new StoryLine[a.Length + b.Length];
            a.CopyTo(all, 0);
            b.CopyTo(all, a.Length);
            return all;
        }

        // Ease the first-person view onto the speaker (PlayerLook is off, so only this moves it; mouse look carries on from here after).
        private IEnumerator TurnView(Transform target)
        {
            var look = player != null ? player.GetComponent<PlayerLook>() : null;
            Camera cam = Camera.main;
            if (look == null || cam == null)
                yield break;

            float startYaw = player.transform.eulerAngles.y;
            float startPitch = -Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            for (float t = 0f; t < lookDuration; t += Time.deltaTime)
            {
                Aim(target, cam, out float yaw, out float pitch);
                float k = Mathf.SmoothStep(0f, 1f, t / lookDuration);
                look.SetLookAngles(Mathf.LerpAngle(startYaw, yaw, k), Mathf.Lerp(startPitch, pitch, k));
                yield return null;
            }
            Aim(target, cam, out float endYaw, out float endPitch);
            look.SetLookAngles(endYaw, endPitch);
        }

        private static void Aim(Transform target, Camera cam, out float yaw, out float pitch)
        {
            Vector3 dir = target.position - cam.transform.position;
            yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
        }
    }
}
