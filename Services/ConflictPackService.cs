using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Aoun.Models;
using Aoun.ViewModels;

namespace Aoun.Services
{
    public class ConflictPackService
    {
        private readonly AounDbContext _db;

        public ConflictPackService(AounDbContext db)
        {
            _db = db;
        }

        // Arabic: ترتيب الباكات حسب الأولوية
        // English: Pack priority order
        private static readonly List<string> PackOrder = new()
        {
            "Pack-LaneChange",
            "Pack-EnteringRoad",
            "Pack-SpecialMove",
            "Pack-IntersectionConfirm",
            "Pack-Intersection",
            "Pack-Position",
            "Pack-OvertakeVsLeftTurn"
        };

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

        // Arabic: أول باك غير محلول لهذا الحادث
        // English: First unresolved pack for this accident
        public async Task<string?> GetNextPendingPackAsync(int accidentId)
        {
            var unresolvedTypes = await _db.AccidentConflicts
                .Where(c => c.AccidentId == accidentId && !c.IsResolved)
                .Select(c => c.ConflictType)
                .ToListAsync();

            var packs = unresolvedTypes
                .Select(MapConflictTypeToPackName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            foreach (var pack in PackOrder)
            {
                if (packs.Contains(pack))
                    return pack;
            }

            return null;
        }

        // Arabic: جلب سؤال من باك محدد حسب الترتيب
        // English: Get pack question by index
        public async Task<ConflictBackWizardViewModel?> GetPackQuestionAsync(int accidentId, int role, string packName, int index)
        {
            int? driverUserId = await GetDriverUserIdByRoleAsync(accidentId, role);
            if (driverUserId == null) return null;

            var questions = await _db.Questions
                .Where(q => q.QuestionType == "ConflictBack" && q.PackName == packName)
                .OrderBy(q => q.SortOrder)
                .ThenBy(q => q.QuestionId)
                .ToListAsync();

            int total = questions.Count;
            if (total == 0) return null;

            if (index < 1) index = 1;
            if (index > total) index = total;

            var q = questions[index - 1];

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

            var vm = new ConflictBackWizardViewModel
            {
                AccidentId = accidentId,
                Role = role,
                PackName = packName,
                QuestionId = q.QuestionId,
                QuestionCode = q.QuestionCode ?? "",
                QuestionTextAr = q.QuestionTextAr ?? "",
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

        // Arabic: حفظ إجابة الباك في نفس جدول Answers
        // English: Save pack answer into same Answers table
        public async Task SavePackAnswerAsync(int accidentId, int role, int questionId, string selectedOptionCode)
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
                    AnsweredAt = System.DateTime.Now
                };
                _db.Answers.Add(row);
            }
            else
            {
                row.SelectedOptionCode = selectedOptionCode;
                row.AnsweredAt = System.DateTime.Now;
            }

            await _db.SaveChangesAsync();
        }

        // Arabic: تحديد هل السائق أكمل الباك كامل
        // English: Did this driver finish the whole pack?
        public async Task<bool> IsPackCompletedByDriverAsync(int accidentId, int role, string packName)
        {
            int? driverUserId = await GetDriverUserIdByRoleAsync(accidentId, role);
            if (driverUserId == null)
                return false;

            var packQuestionIds = await _db.Questions
                .Where(q => q.QuestionType == "ConflictBack" && q.PackName == packName)
                .Select(q => q.QuestionId)
                .ToListAsync();

            if (!packQuestionIds.Any())
                return true;

            var answeredCount = await _db.Answers
                .Where(a => a.AccidentId == accidentId &&
                            a.DriverUserId == driverUserId.Value &&
                            packQuestionIds.Contains(a.QuestionId))
                .CountAsync();

            return answeredCount == packQuestionIds.Count;
        }

        // Arabic: بعد انتهاء الباك للطرفين نعلّم التعارضات المرتبطة به كمحلولة
        // English: Mark all conflicts belonging to this pack as resolved
        public async Task MarkPackConflictsResolvedAsync(int accidentId, string packName)
        {
            var allRows = await _db.AccidentConflicts
                .Where(c => c.AccidentId == accidentId)
                .ToListAsync();

            var rows = allRows
                .Where(c => MapConflictTypeToPackName(c.ConflictType) == packName)
                .ToList();

            foreach (var row in rows)
                row.IsResolved = true;

            await _db.SaveChangesAsync();
        }

        private string? MapConflictTypeToPackName(ConflictType type)
        {
            return type switch
            {
                ConflictType.LaneChange => "Pack-LaneChange",
                ConflictType.EnteringRoad => "Pack-EnteringRoad",
                ConflictType.SpecialMove => "Pack-SpecialMove",
                ConflictType.IntersectionControl => "Pack-Intersection",
                ConflictType.IntersectionCompliance => "Pack-Intersection",
                ConflictType.IntersectionEntryFirst => "Pack-Intersection",
                ConflictType.Position => "Pack-Position",
                ConflictType.Overtake => "Pack-OvertakeVsLeftTurn",
                _ => null
            };
        }

        public async Task<bool> IsPackCompletedByBothDriversAsync(int accidentId, string packName)
        {
            var qIds = await _db.Questions
                .Where(q => q.QuestionType == "ConflictBack" && q.PackName == packName)
                .Select(q => q.QuestionId)
                .ToListAsync();

            if (!qIds.Any())
                return true;

            int? driver1UserId = await GetDriverUserIdByRoleAsync(accidentId, 1);
            int? driver2UserId = await GetDriverUserIdByRoleAsync(accidentId, 2);

            if (driver1UserId == null || driver2UserId == null)
                return false;

            var d1Count = await _db.Answers
                .Where(a => a.AccidentId == accidentId &&
                            a.DriverUserId == driver1UserId.Value &&
                            qIds.Contains(a.QuestionId))
                .CountAsync();

            var d2Count = await _db.Answers
                .Where(a => a.AccidentId == accidentId &&
                            a.DriverUserId == driver2UserId.Value &&
                            qIds.Contains(a.QuestionId))
                .CountAsync();

            return d1Count == qIds.Count && d2Count == qIds.Count;
        }

        public async Task ClearConflictBackAnswersAsync(int accidentId)
        {
            var packQuestionIds = await _db.Questions
                .Where(q => q.QuestionType == "ConflictBack")
                .Select(q => q.QuestionId)
                .ToListAsync();

            var oldAnswers = await _db.Answers
                .Where(a => a.AccidentId == accidentId && packQuestionIds.Contains(a.QuestionId))
                .ToListAsync();

            if (oldAnswers.Any())
            {
                _db.Answers.RemoveRange(oldAnswers);
                await _db.SaveChangesAsync();
            }
        }
    }
}