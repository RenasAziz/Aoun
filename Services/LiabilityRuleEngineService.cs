using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Aoun.Models;

namespace Aoun.Services
{
    // Arabic: البيانات المجمعة اللازمة لتقييم القواعد
    // English: Aggregated data needed for rule evaluation
    public class RuleEvaluationContext
    {
        public int AccidentId { get; set; }

        public Dictionary<string, string> Driver1Answers { get; set; } = new();
        public Dictionary<string, string> Driver2Answers { get; set; } = new();

        public List<AccidentConflict> Conflicts { get; set; } = new();
    }

    // Arabic: نتيجة تشغيل محرك القواعد
    // English: Final result returned by the rule engine
    public class RuleEvaluationResult
    {
        public string RuleId { get; set; } = "";
        public string AccidentClassification { get; set; } = "";

        public int FaultPercentDriver1 { get; set; }
        public int FaultPercentDriver2 { get; set; }

        public decimal BaseConfidenceScore { get; set; }
        public decimal ConflictPenaltyScore { get; set; }
        public decimal EvidenceBonusScore { get; set; }
        public decimal FinalConfidenceScore { get; set; }

        public string FinalConfidenceLabel { get; set; } = "";
        public string DecisionExplanation { get; set; } = "";

        public bool IsMatched { get; set; }
    }

    public class LiabilityRuleEngineService
    {
        private readonly AounDbContext _db;
        private readonly NotificationService _notificationService;

        public LiabilityRuleEngineService(AounDbContext db, NotificationService notificationService)
        {
            _db = db;
            _notificationService = notificationService;
        }

        // =========================================================
        // Helpers: resolve real driver user id from accident + role
        // =========================================================
        private async Task<int?> GetDriverUserIdByRoleAsync(int accidentId, int role)
        {
            if (role != 1 && role != 2)
                return null;

            return await _db.AccidentSessionParticipants
                .Where(p => p.AccidentId == accidentId && p.Role == role)
                .OrderBy(p => p.Id)
                .Select(p => (int?)p.DriverUserId)
                .FirstOrDefaultAsync();
        }

        // =========================================================
        // Public: تقييم الحادث وإرجاع أول قاعدة مطابقة حسب الأولوية
        // English: Evaluate accident and return first matched rule by priority
        // =========================================================
        public async Task<RuleEvaluationResult> EvaluateAsync(int accidentId)
        {
            var context = await BuildContextAsync(accidentId);

            var result =
        TryEvaluateR2(context)
     ?? TryEvaluateR3(context)
     ?? TryEvaluateR4(context)
     ?? TryEvaluateR9(context)
     ?? TryEvaluateR1(context)
     ?? TryEvaluateR10(context)
     ?? TryEvaluateR11(context)
     ?? TryEvaluateR12(context)
     ?? TryEvaluateR5(context)
     ?? TryEvaluateR7(context)
     ?? TryEvaluateR8(context)
     ?? BuildFallbackResult(context);

        ApplyConfidence(result, context);

            return result;
        }

        // =========================================================
        // Public: حفظ النتيجة داخل Accident_Report
        // English: Save result into Accident_Report
        // =========================================================
        public async Task SaveResultAsync(int accidentId, RuleEvaluationResult result)
        {
            var report = await _db.AccidentReports
                .FirstOrDefaultAsync(r => r.AccidentId == accidentId);

            bool isNewReport = report == null;

            if (report == null)
            {
                report = new AccidentReport
                {
                    AccidentId = accidentId,
                    CreatedAt = DateTime.Now
                };

                _db.AccidentReports.Add(report);
            }

            report.FaultPercentDriver1 = result.FaultPercentDriver1;
            report.FaultPercentDriver2 = result.FaultPercentDriver2;
            report.ApprovalStatus = "قيد المراجعة";
            report.Summary = result.AccidentClassification;

            report.RuleId = result.RuleId;
            report.AccidentClassification = result.AccidentClassification;
            report.BaseConfidenceScore = result.BaseConfidenceScore;
            report.ConflictPenaltyScore = result.ConflictPenaltyScore;
            report.EvidenceBonusScore = result.EvidenceBonusScore;
            report.FinalConfidenceScore = result.FinalConfidenceScore;
            report.FinalConfidenceLabel = result.FinalConfidenceLabel;
            report.DecisionExplanation = result.DecisionExplanation;

            await _db.SaveChangesAsync();

            if (isNewReport)
            {
                var inspectorIds = await _db.Users
                    .Where(u => u.Role != null && u.Role.ToLower() == "inspector")
                    .Select(u => u.UserId)
                    .ToListAsync();

                if (inspectorIds.Count > 0)
                {
                    await _notificationService.CreateForUsersAsync(
                        inspectorIds,
                        "تقرير جديد",
                        $"تم إنشاء تقرير جديد للحادث رقم ACC-{accidentId:000000} وهو بانتظار المراجعة.",
                        "NewReportForInspector",
                        accidentId
                    );
                }
            }
        }

        // =========================================================
        // Context Builder
        // =========================================================
        private async Task<RuleEvaluationContext> BuildContextAsync(int accidentId)
        {
            int? driver1UserId = await GetDriverUserIdByRoleAsync(accidentId, 1);
            int? driver2UserId = await GetDriverUserIdByRoleAsync(accidentId, 2);

            var ctx = new RuleEvaluationContext
            {
                AccidentId = accidentId,
                Driver1Answers = driver1UserId.HasValue
                    ? await LoadAnswersAsync(accidentId, driver1UserId.Value)
                    : new Dictionary<string, string>(),
                Driver2Answers = driver2UserId.HasValue
                    ? await LoadAnswersAsync(accidentId, driver2UserId.Value)
                    : new Dictionary<string, string>(),
                Conflicts = await _db.AccidentConflicts
                    .Where(c => c.AccidentId == accidentId)
                    .ToListAsync()
            };

            return ctx;
        }

        private async Task<Dictionary<string, string>> LoadAnswersAsync(int accidentId, int driverUserId)
        {
            return await _db.Answers
                .Where(a => a.AccidentId == accidentId && a.DriverUserId == driverUserId)
                .Join(_db.Questions,
                    a => a.QuestionId,
                    q => q.QuestionId,
                    (a, q) => new { q.QuestionCode, a.SelectedOptionCode })
                .Where(x => x.QuestionCode != null && x.SelectedOptionCode != null)
                .ToDictionaryAsync(x => x.QuestionCode!, x => x.SelectedOptionCode!);
        }

        // =========================================================
        // Helpers
        // =========================================================
        private RuleEvaluationResult BuildSingleFaultResult(
            string ruleId,
            string classification,
            int faultDriver1,
            int faultDriver2,
            string explanation)
        {
            return new RuleEvaluationResult
            {
                IsMatched = true,
                RuleId = ruleId,
                AccidentClassification = classification,
                FaultPercentDriver1 = faultDriver1,
                FaultPercentDriver2 = faultDriver2,
                DecisionExplanation = explanation
            };
        }

        private RuleEvaluationResult BuildSharedFaultResult(
            string ruleId,
            string classification,
            string explanation)
        {
            return new RuleEvaluationResult
            {
                IsMatched = true,
                RuleId = ruleId,
                AccidentClassification = classification,
                FaultPercentDriver1 = 50,
                FaultPercentDriver2 = 50,
                DecisionExplanation = explanation
            };
        }

        // =========================================================
        // Rule R2 - Entering Road Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR2(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool d1EnteringRoad = d1.TryGetValue("CQ2", out var d1Cq2) && d1Cq2 == "CQ2_YES";
            bool d2EnteringRoad = d2.TryGetValue("CQ2", out var d2Cq2) && d2Cq2 == "CQ2_YES";

            if (!d1EnteringRoad && !d2EnteringRoad)
                return null;

            if (d1EnteringRoad && !d2EnteringRoad)
            {
                return BuildSingleFaultResult(
                    "R2",
                    "حادث دخول إلى طريق رئيسي",
                    100,
                    0,
                    "تم تصنيف الحادث كحادث دخول إلى طريق رئيسي، لأن إجابات السائق الأول تشير إلى أنه كان يدخل إلى الطريق وقت وقوع الحادث. في هذه الحالة تكون مسؤولية التأكد من خلو الطريق وإعطاء الأولوية على السائق الداخل إلى الطريق."
                );
            }

            if (!d1EnteringRoad && d2EnteringRoad)
            {
                return BuildSingleFaultResult(
                    "R2",
                    "حادث دخول إلى طريق رئيسي",
                    0,
                    100,
                    "تم تصنيف الحادث كحادث دخول إلى طريق رئيسي، لأن إجابات السائق الثاني تشير إلى أنه كان يدخل إلى الطريق وقت وقوع الحادث. في هذه الحالة تكون مسؤولية التأكد من خلو الطريق وإعطاء الأولوية على السائق الداخل إلى الطريق."
                );
            }

            return BuildSharedFaultResult(
                "R2",
                "حادث دخول إلى طريق رئيسي غير محسوم",
                "أفادت إجابات الطرفين بأن كلًّا منهما كان يدخل إلى الطريق وقت وقوع الحادث، لذلك لا يمكن تحميل المسؤولية كاملة على أحد الطرفين اعتمادًا على هذه الإجابة وحدها، وتم اعتبار الحالة غير محسومة بشكل كامل."
            );
        }

        // =========================================================
        // Rule R3 - Reversing Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR3(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool d1Reversing = d1.TryGetValue("CQ3", out var d1Cq3) && d1Cq3 == "CQ3_REVERSING";
            bool d2Reversing = d2.TryGetValue("CQ3", out var d2Cq3) && d2Cq3 == "CQ3_REVERSING";

            if (!d1Reversing && !d2Reversing)
                return null;

            if (d1Reversing && !d2Reversing)
            {
                return BuildSingleFaultResult(
                    "R3",
                    "حادث أثناء الرجوع للخلف",
                    100,
                    0,
                    "تم تصنيف الحادث كحادث أثناء الرجوع للخلف، لأن إجابات السائق الأول تشير إلى أنه كان يرجع بالمركبة وقت الحادث. والرجوع للخلف يتطلب التأكد الكامل من خلو المسار قبل الحركة."
                );
            }

            if (!d1Reversing && d2Reversing)
            {
                return BuildSingleFaultResult(
                    "R3",
                    "حادث أثناء الرجوع للخلف",
                    0,
                    100,
                    "تم تصنيف الحادث كحادث أثناء الرجوع للخلف، لأن إجابات السائق الثاني تشير إلى أنه كان يرجع بالمركبة وقت الحادث. والرجوع للخلف يتطلب التأكد الكامل من خلو المسار قبل الحركة."
                );
            }

            return BuildSharedFaultResult(
                "R3",
                "حادث رجوع للخلف غير محسوم",
                "أفادت إجابات الطرفين بأن كلًّا منهما كان في حالة رجوع للخلف وقت الحادث، لذلك لا يمكن تحديد طرف واحد مسؤول بشكل منفرد من هذه الإجابات وحدها، وتم اعتبار الحالة غير محسومة بشكل كامل."
            );
        }

        // =========================================================
        // Rule R4 - U-Turn Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR4(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool d1UTurn = d1.TryGetValue("CQ3", out var d1Cq3) && d1Cq3 == "CQ3_UTURN";
            bool d2UTurn = d2.TryGetValue("CQ3", out var d2Cq3) && d2Cq3 == "CQ3_UTURN";

            if (!d1UTurn && !d2UTurn)
                return null;

            if (d1UTurn && !d2UTurn)
            {
                return BuildSingleFaultResult(
                    "R4",
                    "حادث أثناء الالتفاف للخلف",
                    100,
                    0,
                    "تم تصنيف الحادث كحادث أثناء الالتفاف للخلف، لأن إجابات السائق الأول تشير إلى أنه كان يقوم بالالتفاف وقت الحادث. هذا النوع من المناورات يتطلب التأكد من خلو الطريق وإعطاء الأولوية للمركبات الأخرى."
                );
            }

            if (!d1UTurn && d2UTurn)
            {
                return BuildSingleFaultResult(
                    "R4",
                    "حادث أثناء الالتفاف للخلف",
                    0,
                    100,
                    "تم تصنيف الحادث كحادث أثناء الالتفاف للخلف، لأن إجابات السائق الثاني تشير إلى أنه كان يقوم بالالتفاف وقت الحادث. هذا النوع من المناورات يتطلب التأكد من خلو الطريق وإعطاء الأولوية للمركبات الأخرى."
                );
            }

            return BuildSharedFaultResult(
                "R4",
                "حادث التفاف للخلف غير محسوم",
                "أفادت إجابات الطرفين بأن كلًّا منهما كان يقوم بالالتفاف للخلف وقت الحادث، لذلك لا يمكن إسناد المسؤولية الكاملة لطرف واحد اعتمادًا على هذه الإجابات وحدها، وتم اعتبار الحالة غير محسومة بشكل كامل."
            );
        }

        // =========================================================
        // Rule R1 - Lane Change Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR1(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool d1LaneChange = d1.TryGetValue("CQ1", out var d1Cq1) &&
                                (d1Cq1 == "CQ1_LEFT" || d1Cq1 == "CQ1_RIGHT");

            bool d2LaneChange = d2.TryGetValue("CQ1", out var d2Cq1) &&
                                (d2Cq1 == "CQ1_LEFT" || d2Cq1 == "CQ1_RIGHT");

            if (!d1LaneChange && !d2LaneChange)
                return null;

            if (d1LaneChange && !d2LaneChange)
            {
                return BuildSingleFaultResult(
                    "R1",
                    "حادث تغيير مسار",
                    100,
                    0,
                    "تم تصنيف الحادث كحادث تغيير مسار، لأن إجابات السائق الأول تشير إلى أنه كان يغيّر مساره وقت وقوع الحادث. وعند تغيير المسار يجب التأكد أولًا من أن المسار الآخر آمن وخالٍ من المركبات."
                );
            }

            if (!d1LaneChange && d2LaneChange)
            {
                return BuildSingleFaultResult(
                    "R1",
                    "حادث تغيير مسار",
                    0,
                    100,
                    "تم تصنيف الحادث كحادث تغيير مسار، لأن إجابات السائق الثاني تشير إلى أنه كان يغيّر مساره وقت وقوع الحادث. وعند تغيير المسار يجب التأكد أولًا من أن المسار الآخر آمن وخالٍ من المركبات."
                );
            }

            return BuildSharedFaultResult(
                "R1",
                "حادث تغيير مسار غير محسوم",
                "أفادت إجابات الطرفين بأن كلًّا منهما كان يغيّر المسار وقت وقوع الحادث، لذلك لا يمكن تحميل المسؤولية كاملة لأحد الطرفين بناءً على هذه الإجابات وحدها، وتم اعتبار الحالة غير محسومة بشكل كامل."
            );
        }

        // =========================================================
        // Rule R10 - Signal Violation Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR10(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool isIntersection =
                (d1.TryGetValue("CQ6", out var d1Cq6) && d1Cq6 == "CQ6_YES") ||
                (d2.TryGetValue("CQ6", out var d2Cq6) && d2Cq6 == "CQ6_YES");

            if (!isIntersection)
                return null;

            bool hasControl =
                (d1.TryGetValue("CQ7", out var d1Cq7) && IsControlledIntersection(d1Cq7)) ||
                (d2.TryGetValue("CQ7", out var d2Cq7) && IsControlledIntersection(d2Cq7));

            if (!hasControl)
                return null;

            bool d1Violated = d1.TryGetValue("CQ8", out var d1Cq8) && d1Cq8 == "CQ8_NO";
            bool d2Violated = d2.TryGetValue("CQ8", out var d2Cq8) && d2Cq8 == "CQ8_NO";

            if (!d1Violated && !d2Violated)
                return null;

            if (d1Violated && !d2Violated)
            {
                return BuildSingleFaultResult(
                    "R10",
                    "حادث مخالفة إشارة أو أولوية",
                    100,
                    0,
                    "تم تصنيف الحادث كحادث مخالفة إشارة أو أولوية داخل تقاطع منظم، لأن إجابات السائق الأول تشير إلى أنه لم يلتزم بوسيلة التحكم الموجودة في التقاطع، مثل الإشارة أو الوقوف أو أولوية المرور."
                );
            }

            if (!d1Violated && d2Violated)
            {
                return BuildSingleFaultResult(
                    "R10",
                    "حادث مخالفة إشارة أو أولوية",
                    0,
                    100,
                    "تم تصنيف الحادث كحادث مخالفة إشارة أو أولوية داخل تقاطع منظم، لأن إجابات السائق الثاني تشير إلى أنه لم يلتزم بوسيلة التحكم الموجودة في التقاطع، مثل الإشارة أو الوقوف أو أولوية المرور."
                );
            }

            return BuildSharedFaultResult(
                "R10",
                "حادث تقاطع مع مخالفة غير محسومة",
                "أفادت إجابات الطرفين بوجود عدم التزام داخل تقاطع منظم، لذلك لا يمكن تحديد طرف واحد كمخالف بشكل قاطع اعتمادًا على هذه الإجابات وحدها، وتم اعتبار الحالة غير محسومة بشكل كامل."
            );
        }

        // =========================================================
        // Rule R11 - Failure to Yield Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR11(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool isIntersection =
                (d1.TryGetValue("CQ6", out var d1Cq6) && d1Cq6 == "CQ6_YES") ||
                (d2.TryGetValue("CQ6", out var d2Cq6) && d2Cq6 == "CQ6_YES");

            if (!isIntersection)
                return null;

            if (d1.TryGetValue("CQ9", out var d1Cq9) &&
                d2.TryGetValue("CQ9", out var d2Cq9))
            {
                if (d1Cq9 == "CQ9_OTHER" && d2Cq9 == "CQ9_ME")
                {
                    return BuildSingleFaultResult(
                        "R11",
                        "حادث عدم إعطاء أولوية",
                        100,
                        0,
                        "تم تصنيف الحادث كحادث عدم إعطاء أولوية، لأن إجابات الطرفين تشير إلى أن السائق الثاني دخل أولًا إلى مسار التقاطع، بينما لم يمنحه السائق الأول الأولوية المطلوبة."
                    );
                }

                if (d1Cq9 == "CQ9_ME" && d2Cq9 == "CQ9_OTHER")
                {
                    return BuildSingleFaultResult(
                        "R11",
                        "حادث عدم إعطاء أولوية",
                        0,
                        100,
                        "تم تصنيف الحادث كحادث عدم إعطاء أولوية، لأن إجابات الطرفين تشير إلى أن السائق الأول دخل أولًا إلى مسار التقاطع، بينما لم يمنحه السائق الثاني الأولوية المطلوبة."
                    );
                }
            }

            return null;
        }

        // =========================================================
        // Rule R12 - Undetermined Intersection
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR12(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool isIntersection =
                (d1.TryGetValue("CQ6", out var d1Cq6) && d1Cq6 == "CQ6_YES") ||
                (d2.TryGetValue("CQ6", out var d2Cq6) && d2Cq6 == "CQ6_YES");

            if (!isIntersection)
                return null;

            return BuildSharedFaultResult(
                "R12",
                "حادث تقاطع غير محسوم",
                "تم تصنيف الحادث كحادث تقاطع غير محسوم، لأن الحادث وقع داخل تقاطع، لكن الإجابات المتوفرة لم تكن كافية لتحديد الأولوية بشكل واضح أو إثبات مخالفة مرورية على أحد الطرفين بدرجة كافية من اليقين."
            );
        }

        // =========================================================
        // Rule R5 - Rear-End Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR5(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            // CQ5 السؤال نصه: "ما موقع مركبتك بالنسبة للمركبة الأخرى؟"
            // لذلك:
            // CQ5_AHEAD  = مركبتي أمام المركبة الأخرى
            // CQ5_BEHIND = مركبتي خلف المركبة الأخرى

            bool d1IsAhead = d1.TryGetValue("CQ5", out var d1Cq5) && d1Cq5 == "CQ5_AHEAD";
            bool d2IsAhead = d2.TryGetValue("CQ5", out var d2Cq5) && d2Cq5 == "CQ5_AHEAD";

            bool d1IsBehind = d1.TryGetValue("CQ5", out d1Cq5) && d1Cq5 == "CQ5_BEHIND";
            bool d2IsBehind = d2.TryGetValue("CQ5", out d2Cq5) && d2Cq5 == "CQ5_BEHIND";

            // الحالة الأوضح:
            // إذا السائق 1 يقول "أنا أمامها" والسائق 2 يقول "أنا خلفها"
            // فهذا يعني السائق 2 هو الخلف => عليه المسؤولية
            if (d1IsAhead && d2IsBehind)
            {
                return BuildSingleFaultResult(
                    "R5",
                    "حادث اصطدام خلفي",
                    0,
                    100,
                    "تم تصنيف الحادث كحادث اصطدام خلفي، لأن إجابات الطرفين تشير إلى أن السائق الأول كان أمام المركبة الأخرى، بينما كان السائق الثاني خلفه، وبالتالي يتحمل السائق الخلفي مسؤولية ترك مسافة كافية والتوقف بأمان."
                );
            }

            // إذا السائق 1 يقول "أنا خلفها" والسائق 2 يقول "أنا أمامها"
            // فهذا يعني السائق 1 هو الخلف => عليه المسؤولية
            if (d1IsBehind && d2IsAhead)
            {
                return BuildSingleFaultResult(
                    "R5",
                    "حادث اصطدام خلفي",
                    100,
                    0,
                    "تم تصنيف الحادث كحادث اصطدام خلفي، لأن إجابات الطرفين تشير إلى أن السائق الثاني كان أمام المركبة الأخرى، بينما كان السائق الأول خلفه، وبالتالي يتحمل السائق الخلفي مسؤولية ترك مسافة كافية والتوقف بأمان."
                );
            }

            // لو فقط طرف واحد أجاب بشكل يوحي أنه كان خلف الآخر
            if (d1IsBehind && !d2IsAhead)
            {
                return BuildSingleFaultResult(
                    "R5",
                    "حادث اصطدام خلفي",
                    100,
                    0,
                    "تم تصنيف الحادث كحادث اصطدام خلفي، لأن إجابة السائق الأول تشير إلى أن مركبته كانت خلف المركبة الأخرى وقت الحادث، وبالتالي يتحمل السائق الخلفي مسؤولية ترك مسافة كافية والتوقف بأمان."
                );
            }

            if (d2IsBehind && !d1IsAhead)
            {
                return BuildSingleFaultResult(
                    "R5",
                    "حادث اصطدام خلفي",
                    0,
                    100,
                    "تم تصنيف الحادث كحادث اصطدام خلفي، لأن إجابة السائق الثاني تشير إلى أن مركبته كانت خلف المركبة الأخرى وقت الحادث، وبالتالي يتحمل السائق الخلفي مسؤولية ترك مسافة كافية والتوقف بأمان."
                );
            }

            // إذا الطرفان قالوا "أمام" أو الطرفان قالوا "خلف" أو كانت الإجابات غير حاسمة
            if (d1IsAhead || d2IsAhead || d1IsBehind || d2IsBehind)
            {
                return BuildSharedFaultResult(
                    "R5",
                    "حادث اصطدام خلفي غير محسوم",
                    "أفادت الإجابات بشأن موضع المركبتين قبل الاصطدام الخلفي بشكل غير حاسم أو متعارض، لذلك لا يمكن تحديد الطرف المسؤول بشكل قاطع من هذه الإجابات وحدها."
                );
            }

            return null;
        }

        // =========================================================
        // Rule R9 - Overtake vs Left-Turn Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR9(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool d1Overtaking = d1.TryGetValue("CQ10", out var d1Cq10) && d1Cq10 == "CQ10_YES";
            bool d2Overtaking = d2.TryGetValue("CQ10", out var d2Cq10) && d2Cq10 == "CQ10_YES";

            bool d1TurningLeft = d1.TryGetValue("CQ1", out var d1Cq1) && d1Cq1 == "CQ1_LEFT";
            bool d2TurningLeft = d2.TryGetValue("CQ1", out var d2Cq1) && d2Cq1 == "CQ1_LEFT";

            if (d1Overtaking && d2TurningLeft)
            {
                return BuildSingleFaultResult(
                    "R9",
                    "حادث تجاوز مع انعطاف لليسار",
                    75,
                    25,
                    "تم تصنيف الحادث كحادث تجاوز مع انعطاف لليسار، لأن إجابات السائق الأول تشير إلى أنه كان في حالة تجاوز، بينما إجابات السائق الثاني تشير إلى انعطافه لليسار. في هذا النوع من الحوادث يتحمل المتجاوز عادة النسبة الأكبر من المسؤولية، مع بقاء جزء من المسؤولية على الطرف المنعطف بحسب ظروف الحركة."
                );
            }

            if (d2Overtaking && d1TurningLeft)
            {
                return BuildSingleFaultResult(
                    "R9",
                    "حادث تجاوز مع انعطاف لليسار",
                    25,
                    75,
                    "تم تصنيف الحادث كحادث تجاوز مع انعطاف لليسار، لأن إجابات السائق الثاني تشير إلى أنه كان في حالة تجاوز، بينما إجابات السائق الأول تشير إلى انعطافه لليسار. في هذا النوع من الحوادث يتحمل المتجاوز عادة النسبة الأكبر من المسؤولية، مع بقاء جزء من المسؤولية على الطرف المنعطف بحسب ظروف الحركة."
                );
            }

            return null;
        }

        // =========================================================
        // Rule R7 - Overtaking Collision
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR7(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool d1Overtaking = d1.TryGetValue("CQ10", out var d1Cq10) && d1Cq10 == "CQ10_YES";
            bool d2Overtaking = d2.TryGetValue("CQ10", out var d2Cq10) && d2Cq10 == "CQ10_YES";

            if (!d1Overtaking && !d2Overtaking)
                return null;

            if (d1Overtaking && !d2Overtaking)
            {
                return BuildSingleFaultResult(
                    "R7",
                    "حادث أثناء التجاوز",
                    100,
                    0,
                    "تم تصنيف الحادث كحادث أثناء التجاوز، لأن إجابات السائق الأول تشير إلى أنه كان يتجاوز وقت وقوع الحادث. والتجاوز من المناورات التي تتطلب التأكد من وضوح الطريق وأمان الحركة قبل التنفيذ."
                );
            }

            if (!d1Overtaking && d2Overtaking)
            {
                return BuildSingleFaultResult(
                    "R7",
                    "حادث أثناء التجاوز",
                    0,
                    100,
                    "تم تصنيف الحادث كحادث أثناء التجاوز، لأن إجابات السائق الثاني تشير إلى أنه كان يتجاوز وقت وقوع الحادث. والتجاوز من المناورات التي تتطلب التأكد من وضوح الطريق وأمان الحركة قبل التنفيذ."
                );
            }

            return BuildSharedFaultResult(
                "R7",
                "حادث تجاوز غير محسوم",
                "أفادت إجابات الطرفين بأن كلًّا منهما كان في حالة تجاوز وقت الحادث، لذلك لا يمكن تحميل المسؤولية الكاملة لطرف واحد اعتمادًا على هذه الإجابات وحدها، وتم اعتبار الحالة غير محسومة بشكل كامل."
            );
        }

        // =========================================================
        // Rule R8 - Side-Impact / Undetermined Position
        // =========================================================
        private RuleEvaluationResult? TryEvaluateR8(RuleEvaluationContext context)
        {
            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            bool d1Beside = d1.TryGetValue("CQ5", out var d1Cq5) && d1Cq5 == "CQ5_BESIDE";
            bool d2Beside = d2.TryGetValue("CQ5", out var d2Cq5) && d2Cq5 == "CQ5_BESIDE";

            bool d1UnknownPosition = d1.TryGetValue("CQ11", out var d1Cq11) && d1Cq11 == "CQ11_NO";
            bool d2UnknownPosition = d2.TryGetValue("CQ11", out var d2Cq11) && d2Cq11 == "CQ11_NO";

            if (d1Beside || d2Beside || d1UnknownPosition || d2UnknownPosition)
            {
                return BuildSharedFaultResult(
                    "R8",
                    "حادث جانبي أو موضع غير محدد",
                    "تم تصنيف الحادث كحادث جانبي أو كحالة موضع غير محدد، لأن الإجابات تشير إلى أن المركبتين كانتا متجاورتين وقت الحادث أو أن موضعهما قبل الاصطدام لم يكن واضحًا بما يكفي لتحديد المسؤولية بشكل دقيق."
                );
            }

            return null;
        }

        // =========================================================
        // Fallback
        // =========================================================
        private RuleEvaluationResult BuildFallbackResult(RuleEvaluationContext context)
        {
            return BuildSharedFaultResult(
                "R50",
                "حادث غير محسوم",
                "لم تنطبق أي قاعدة مرورية واضحة على الحادث بناءً على الإجابات المتوفرة حاليًا، لذلك تم تصنيف الحالة كحادث غير محسوم إلى حين توفر معلومات إضافية أو أدلة داعمة."
            );
        }

        private bool IsControlledIntersection(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return false;

            return code == "CQ7_LIGHT"
                || code == "CQ7_STOP"
                || code == "CQ7_PRIORITY";
        }

        // =========================================================
        // Confidence
        // =========================================================
        private void ApplyConfidence(RuleEvaluationResult result, RuleEvaluationContext context)
        {
            result.BaseConfidenceScore = GetBaseConfidence(result.RuleId);
            result.ConflictPenaltyScore = CalculateConflictPenalty(context);
            result.EvidenceBonusScore = CalculateEvidenceBonus(result, context);

            var finalScore = result.BaseConfidenceScore
                           - result.ConflictPenaltyScore
                           + result.EvidenceBonusScore;

            if (finalScore < 0.30m) finalScore = 0.30m;
            if (finalScore > 0.98m) finalScore = 0.98m;

            result.FinalConfidenceScore = finalScore;
            result.FinalConfidenceLabel = ToConfidenceLabel(finalScore);

           
        }

        private decimal GetBaseConfidence(string ruleId)
        {
            return ruleId switch
            {
                "R2" => 0.95m,
                "R3" => 0.95m,
                "R4" => 0.95m,
                "R1" => 0.95m,

                "R10" => 0.85m,
                "R11" => 0.85m,
                "R12" => 0.85m,

                "R5" => 0.75m,
                "R9" => 0.75m,
                "R7" => 0.75m,
                "R8" => 0.75m,

                "R50" => 0.50m,
                _ => 0.50m
            };
        }

        private decimal CalculateConflictPenalty(RuleEvaluationContext context)
        {
            decimal total = 0m;

            var uniqueConflicts = context.Conflicts
                .GroupBy(c => c.ConflictType)
                .Select(g => g.First())
                .ToList();

            foreach (var conflict in uniqueConflicts)
            {
                total += conflict.ConflictType switch
                {
                    ConflictType.LaneChange => 0.15m,
                    ConflictType.EnteringRoad => 0.15m,
                    ConflictType.SpecialMove => 0.15m,
                    ConflictType.Overtake => 0.15m,

                    ConflictType.IntersectionControl => 0.10m,
                    ConflictType.IntersectionCompliance => 0.10m,
                    ConflictType.IntersectionEntryFirst => 0.10m,

                    ConflictType.Position => 0.05m,
                    _ => 0.05m
                };
            }

            return total;
        }

        private decimal CalculateEvidenceBonus(RuleEvaluationResult result, RuleEvaluationContext context)
        {
            decimal bonus = 0m;

            var d1 = context.Driver1Answers;
            var d2 = context.Driver2Answers;

            switch (result.RuleId)
            {
                case "R2":
                    if (d1.TryGetValue("CQ2", out var d1Cq2) && d1Cq2 == "CQ2_YES") bonus += 0.10m;
                    if (d2.TryGetValue("CQ2", out var d2Cq2) && d2Cq2 == "CQ2_YES") bonus += 0.10m;
                    if (d1.TryGetValue("M2", out var d1M2) && d1M2 == "M2_YES") bonus += 0.10m;
                    if (d2.TryGetValue("M2", out var d2M2) && d2M2 == "M2_YES") bonus += 0.10m;
                    break;

                case "R3":
                    if (d1.TryGetValue("CQ3", out var d1Cq3) && d1Cq3 == "CQ3_REVERSING") bonus += 0.10m;
                    if (d2.TryGetValue("CQ3", out var d2Cq3) && d2Cq3 == "CQ3_REVERSING") bonus += 0.10m;
                    if (d1.TryGetValue("M3", out var d1M3) && d1M3 == "M3_YES") bonus += 0.10m;
                    if (d2.TryGetValue("M3", out var d2M3) && d2M3 == "M3_YES") bonus += 0.10m;
                    break;

                case "R4":
                    if (d1.TryGetValue("CQ3", out var d1Cq3U) && d1Cq3U == "CQ3_UTURN") bonus += 0.10m;
                    if (d2.TryGetValue("CQ3", out var d2Cq3U) && d2Cq3U == "CQ3_UTURN") bonus += 0.10m;
                    if (d1.TryGetValue("M3", out var d1M3U) && d1M3U == "M3_YES") bonus += 0.10m;
                    if (d2.TryGetValue("M3", out var d2M3U) && d2M3U == "M3_YES") bonus += 0.10m;
                    break;

                case "R1":
                    if (d1.TryGetValue("CQ1", out var d1Cq1) && (d1Cq1 == "CQ1_LEFT" || d1Cq1 == "CQ1_RIGHT")) bonus += 0.10m;
                    if (d2.TryGetValue("CQ1", out var d2Cq1) && (d2Cq1 == "CQ1_LEFT" || d2Cq1 == "CQ1_RIGHT")) bonus += 0.10m;
                    if (d1.TryGetValue("M1", out var d1M1) && d1M1 == "M1_YES") bonus += 0.10m;
                    if (d2.TryGetValue("M1", out var d2M1) && d2M1 == "M1_YES") bonus += 0.10m;
                    break;

                case "R10":
                    if (d1.TryGetValue("CQ6", out var d1Cq6Int) && d1Cq6Int == "CQ6_YES") bonus += 0.05m;
                    if (d2.TryGetValue("CQ6", out var d2Cq6Int) && d2Cq6Int == "CQ6_YES") bonus += 0.05m;

                    if (d1.TryGetValue("CQ7", out var d1Cq7Ctrl) && IsControlledIntersection(d1Cq7Ctrl)) bonus += 0.10m;
                    if (d2.TryGetValue("CQ7", out var d2Cq7Ctrl) && IsControlledIntersection(d2Cq7Ctrl)) bonus += 0.10m;

                    if (d1.TryGetValue("CQ8", out var d1Cq8No) && d1Cq8No == "CQ8_NO") bonus += 0.10m;
                    if (d2.TryGetValue("CQ8", out var d2Cq8No) && d2Cq8No == "CQ8_NO") bonus += 0.10m;
                    break;

                case "R11":
                    if (d1.TryGetValue("CQ6", out var d1Cq6Yield) && d1Cq6Yield == "CQ6_YES") bonus += 0.05m;
                    if (d2.TryGetValue("CQ6", out var d2Cq6Yield) && d2Cq6Yield == "CQ6_YES") bonus += 0.05m;

                    if (d1.TryGetValue("CQ9", out var d1Cq9) && (d1Cq9 == "CQ9_ME" || d1Cq9 == "CQ9_OTHER")) bonus += 0.10m;
                    if (d2.TryGetValue("CQ9", out var d2Cq9) && (d2Cq9 == "CQ9_ME" || d2Cq9 == "CQ9_OTHER")) bonus += 0.10m;
                    break;

                case "R12":
                    if (d1.TryGetValue("CQ6", out var d1Cq6Und) && d1Cq6Und == "CQ6_YES") bonus += 0.05m;
                    if (d2.TryGetValue("CQ6", out var d2Cq6Und) && d2Cq6Und == "CQ6_YES") bonus += 0.05m;
                    break;

                case "R5":
                    if (d1.TryGetValue("CQ5", out var d1Rear) && d1Rear == "CQ5_AHEAD") bonus += 0.10m;
                    if (d2.TryGetValue("CQ5", out var d2Rear) && d2Rear == "CQ5_AHEAD") bonus += 0.10m;
                    break;

                case "R9":
                    if (d1.TryGetValue("CQ10", out var d1Ovlt) && d1Ovlt == "CQ10_YES") bonus += 0.10m;
                    if (d2.TryGetValue("CQ10", out var d2Ovlt) && d2Ovlt == "CQ10_YES") bonus += 0.10m;

                    if (d1.TryGetValue("CQ1", out var d1Left) && d1Left == "CQ1_LEFT") bonus += 0.10m;
                    if (d2.TryGetValue("CQ1", out var d2Left) && d2Left == "CQ1_LEFT") bonus += 0.10m;
                    break;

                case "R7":
                    if (d1.TryGetValue("CQ10", out var d1OvertakeOnly) && d1OvertakeOnly == "CQ10_YES") bonus += 0.10m;
                    if (d2.TryGetValue("CQ10", out var d2OvertakeOnly) && d2OvertakeOnly == "CQ10_YES") bonus += 0.10m;

                    if (d1.TryGetValue("M5", out var d1M5) && d1M5 == "M5_YES") bonus += 0.10m;
                    if (d2.TryGetValue("M5", out var d2M5) && d2M5 == "M5_YES") bonus += 0.10m;
                    break;

                case "R8":
                    if (d1.TryGetValue("CQ5", out var d1BesideEv) && d1BesideEv == "CQ5_BESIDE") bonus += 0.05m;
                    if (d2.TryGetValue("CQ5", out var d2BesideEv) && d2BesideEv == "CQ5_BESIDE") bonus += 0.05m;

                    if (d1.TryGetValue("CQ11", out var d1UnknownEv) && d1UnknownEv == "CQ11_NO") bonus += 0.05m;
                    if (d2.TryGetValue("CQ11", out var d2UnknownEv) && d2UnknownEv == "CQ11_NO") bonus += 0.05m;
                    break;
            }

            return bonus;
        }

        private string ToConfidenceLabel(decimal score)
        {
            if (score >= 0.80m) return "مرتفعة";
            if (score >= 0.60m) return "متوسطة";
            if (score >= 0.40m) return "منخفضة";
            return "منخفضة جدًا";
        }
    }
}