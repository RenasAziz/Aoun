using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Aoun.Models;
using Aoun.ViewModels;

namespace Aoun.Services
{
    public class QuestionnaireService
    {
        private readonly AounDbContext _db;

        public QuestionnaireService(AounDbContext db)
        {
            _db = db;
        }

        // =========================================================
        // DTO داخلي بدل dynamic
        // =========================================================
        private sealed class CoreQuestionDto
        {
            public int QuestionId { get; set; }
            public string QuestionCode { get; set; } = "";
            public string QuestionTextAr { get; set; } = "";
            public int SortOrder { get; set; }
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
        // Public: جلب سؤال Core حسب (Index) لكن بعد تطبيق الـ Skip rules
        // =========================================================
        public async Task<QuestionsWizardViewModel?> GetCoreQuestionAsync(int accidentId, int role, int index)
        {
            int? driverUserId = await GetDriverUserIdByRoleAsync(accidentId, role);
            if (driverUserId == null) return null;

            var answersByCode = await LoadAnswersByQuestionCodeAsync(accidentId, driverUserId.Value);
            bool cq6AnsweredAnyDriver = await IsAnyDriverAnsweredAsync(accidentId, "CQ6");
            bool cq6YesAnyDriver = await IsAnyDriverAnsweredYesAsync(accidentId, "CQ6", "CQ6_YES");

            var effectiveQuestions = await GetEffectiveCoreQuestionsAsync(
                answersByCode,
                cq6AnsweredAnyDriver,
                cq6YesAnyDriver);

            int total = effectiveQuestions.Count;
            if (total == 0) return null;

            if (index < 1) index = 1;
            if (index > total) index = total;

            var q = effectiveQuestions[index - 1];

            var options = await _db.QuestionOptions
                .Where(o => o.QuestionId == q.QuestionId)
                .OrderBy(o => o.SortOrder)
                .ThenBy(o => o.OptionId)
                .Select(o => new OptionItemViewModel
                {
                    Code = o.OptionCode,
                    TextAr = o.OptionTextAr
                })
                .ToListAsync();

            var vm = new QuestionsWizardViewModel
            {
                AccidentId = accidentId,
                Role = role,
                QuestionId = q.QuestionId,
                QuestionCode = q.QuestionCode,
                QuestionTextAr = q.QuestionTextAr,
                Options = options,
                Index = index,
                Total = total
            };

            vm.SelectedOptionCode = await _db.Answers
                .Where(a => a.AccidentId == accidentId &&
                            a.DriverUserId == driverUserId.Value &&
                            a.QuestionId == q.QuestionId)
                .Select(a => a.SelectedOptionCode)
                .FirstOrDefaultAsync();

            return vm;
        }

        // =========================================================
        // Public: حفظ إجابة (Upsert)
        // =========================================================
        public async Task SaveAnswerAsync(int accidentId, int role, int questionId, string selectedOptionCode)
        {
            int? driverUserId = await GetDriverUserIdByRoleAsync(accidentId, role);
            if (driverUserId == null)
                return;

            var row = await _db.Answers.FirstOrDefaultAsync(a =>
                a.AccidentId == accidentId &&
                a.DriverUserId == driverUserId.Value &&
                a.QuestionId == questionId);

            if (row == null)
            {
                row = new Answer
                {
                    AccidentId = accidentId,
                    DriverUserId = driverUserId.Value,
                    QuestionId = questionId,
                    SelectedOptionCode = selectedOptionCode,
                    AnsweredAt = DateTime.Now
                };
                _db.Answers.Add(row);
            }
            else
            {
                row.SelectedOptionCode = selectedOptionCode;
                row.AnsweredAt = DateTime.Now;
            }

            await _db.SaveChangesAsync();
        }

        // =========================================================
        // Helpers
        // =========================================================

        private async Task<Dictionary<string, string>> LoadAnswersByQuestionCodeAsync(int accidentId, int driverUserId)
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

        private async Task<List<CoreQuestionDto>> GetEffectiveCoreQuestionsAsync(
            Dictionary<string, string> answersByCode,
            bool cq6AnsweredAnyDriver,
            bool cq6YesAnyDriver)
        {
            var allCore = await _db.Questions
                .Where(q => q.QuestionType == "Core")
                .OrderBy(q => q.SortOrder)
                .ThenBy(q => q.QuestionId)
                .Select(q => new CoreQuestionDto
                {
                    QuestionId = q.QuestionId,
                    QuestionCode = q.QuestionCode ?? "",
                    QuestionTextAr = q.QuestionTextAr ?? "",
                    SortOrder = q.SortOrder
                })
                .ToListAsync();

            var effective = new List<CoreQuestionDto>();

            foreach (var q in allCore)
            {
                if (ShouldSkipCoreQuestion(q.QuestionCode, answersByCode, cq6AnsweredAnyDriver, cq6YesAnyDriver))
                    continue;

                effective.Add(q);
            }

            return effective;
        }

        // =========================================================
        // ⭐ Skip/Branching logic for Core questions
        // =========================================================
        private static bool ShouldSkipCoreQuestion(
            string questionCode,
            Dictionary<string, string> answersByCode,
            bool cq6AnsweredAnyDriver,
            bool cq6YesAnyDriver)
        {
            var intersectionDetailQuestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CQ7", "CQ8", "CQ9"
            };

            if (!intersectionDetailQuestions.Contains(questionCode))
                return false;

            if (!answersByCode.TryGetValue("CQ6", out var cq6ForCurrentDriver))
                return true;

            if (cq6YesAnyDriver)
                return false;

            return true;
        }

        // =========================================================
        // Mirror Wizard
        // =========================================================
        public async Task<QuestionsWizardViewModel?> GetMirrorQuestionAsync(int accidentId, int role, int index)
        {
            int? driverUserId = await GetDriverUserIdByRoleAsync(accidentId, role);
            if (driverUserId == null) return null;

            var currentDriverAnswers = await LoadAnswersByQuestionCodeAsync(accidentId, driverUserId.Value);

            bool cq10YesAnyDriver = await IsAnyDriverAnsweredYesAsync(accidentId, "CQ10", "CQ10_YES");
            bool cq6YesAnyDriver = await IsAnyDriverAnsweredYesAsync(accidentId, "CQ6", "CQ6_YES");

            var effective = await GetEffectiveMirrorQuestionsAsync(
                currentDriverAnswers,
                cq10YesAnyDriver,
                cq6YesAnyDriver);

            int total = effective.Count;
            if (total == 0) return null;

            if (index < 1) index = 1;
            if (index > total) index = total;

            var q = effective[index - 1];

            var options = await _db.QuestionOptions
                .Where(o => o.QuestionId == q.QuestionId)
                .OrderBy(o => o.SortOrder)
                .ThenBy(o => o.OptionId)
                .Select(o => new OptionItemViewModel
                {
                    Code = o.OptionCode,
                    TextAr = o.OptionTextAr
                })
                .ToListAsync();

            var vm = new QuestionsWizardViewModel
            {
                AccidentId = accidentId,
                Role = role,
                QuestionId = q.QuestionId,
                QuestionCode = q.QuestionCode,
                QuestionTextAr = q.QuestionTextAr,
                Options = options,
                Index = index,
                Total = total
            };

            vm.SelectedOptionCode = await _db.Answers
                .Where(a => a.AccidentId == accidentId &&
                            a.DriverUserId == driverUserId.Value &&
                            a.QuestionId == q.QuestionId)
                .Select(a => a.SelectedOptionCode)
                .FirstOrDefaultAsync();

            return vm;
        }

        private async Task<List<CoreQuestionDto>> GetEffectiveMirrorQuestionsAsync(
            Dictionary<string, string> currentDriverAnswers,
            bool cq10YesAnyDriver,
            bool cq6YesAnyDriver)
        {
            var allMirror = await _db.Questions
                .Where(q => q.QuestionType == "Mirror")
                .OrderBy(q => q.SortOrder)
                .ThenBy(q => q.QuestionId)
                .Select(q => new CoreQuestionDto
                {
                    QuestionId = q.QuestionId,
                    QuestionCode = q.QuestionCode ?? "",
                    QuestionTextAr = q.QuestionTextAr ?? "",
                    SortOrder = q.SortOrder
                })
                .ToListAsync();

            var effective = new List<CoreQuestionDto>();

            foreach (var q in allMirror)
            {
                if (ShouldSkipMirrorQuestion(q.QuestionCode, currentDriverAnswers, cq10YesAnyDriver, cq6YesAnyDriver))
                    continue;

                effective.Add(q);
            }

            return effective;
        }

        public async Task<int> GetNextCoreIndexAsync(int accidentId, int role, int currentQuestionId)
        {
            int? driverUserId = await GetDriverUserIdByRoleAsync(accidentId, role);
            if (driverUserId == null) return 1;

            var answersByCode = await LoadAnswersByQuestionCodeAsync(accidentId, driverUserId.Value);
            bool cq6AnsweredAnyDriver = await IsAnyDriverAnsweredAsync(accidentId, "CQ6");
            bool cq6YesAnyDriver = await IsAnyDriverAnsweredYesAsync(accidentId, "CQ6", "CQ6_YES");

            var effective = await GetEffectiveCoreQuestionsAsync(
                answersByCode,
                cq6AnsweredAnyDriver,
                cq6YesAnyDriver);

            if (effective.Count == 0) return 1;

            int pos = effective.FindIndex(x => x.QuestionId == currentQuestionId);
            if (pos < 0) return 1;

            int next = pos + 2;
            if (next > effective.Count) next = effective.Count;

            return next;
        }

        private static bool ShouldSkipMirrorQuestion(
            string questionCode,
            Dictionary<string, string> currentDriverAnswers,
            bool cq10YesAnyDriver,
            bool cq6YesAnyDriver)
        {
            if (questionCode.Equals("M4", StringComparison.OrdinalIgnoreCase))
            {
                return !cq6YesAnyDriver;
            }

            if (questionCode.Equals("M5", StringComparison.OrdinalIgnoreCase))
            {
                return !cq10YesAnyDriver;
            }

            return false;
        }

        private async Task<bool> IsAnyDriverAnsweredYesAsync(int accidentId, string questionCode, string yesOptionCode)
        {
            return await _db.Answers
                .Where(a => a.AccidentId == accidentId && a.SelectedOptionCode != null)
                .Join(_db.Questions,
                      a => a.QuestionId,
                      q => q.QuestionId,
                      (a, q) => new { a.SelectedOptionCode, q.QuestionCode })
                .AnyAsync(x => x.QuestionCode == questionCode &&
                               x.SelectedOptionCode == yesOptionCode);
        }

        private async Task<bool> IsAnyDriverAnsweredAsync(int accidentId, string questionCode)
        {
            return await _db.Answers
                .Where(a => a.AccidentId == accidentId && a.SelectedOptionCode != null)
                .Join(_db.Questions,
                      a => a.QuestionId,
                      q => q.QuestionId,
                      (a, q) => new { q.QuestionCode })
                .AnyAsync(x => x.QuestionCode == questionCode);
        }
    }
}