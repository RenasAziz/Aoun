using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Aoun.Models;

namespace Aoun.Services
{
    public class ConflictService
    {
        private readonly AounDbContext _db;

        public ConflictService(AounDbContext db)
        {
            _db = db;
        }

        // =====================================================
        // Helpers: resolve real driver user id from accident + role
        // =====================================================
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

        // Arabic: Alias method to match existing controller call name
        // English: Keep backward compatibility with the controller call
        public async Task DetectAndUpsertConflictsAsync(int accidentId)
        {
            await DetectAndStoreConflictsAsync(accidentId);
        }

        // =====================================================
        // Detect conflicts and store them in AccidentConflict
        // =====================================================
        public async Task DetectAndStoreConflictsAsync(int accidentId)
        {
            int? driver1 = await GetDriverUserIdByRoleAsync(accidentId, 1);
            int? driver2 = await GetDriverUserIdByRoleAsync(accidentId, 2);

            if (driver1 == null || driver2 == null)
                return;

            var d1 = await LoadAnswers(accidentId, driver1.Value);
            var d2 = await LoadAnswers(accidentId, driver2.Value);

            var conflicts = new List<AccidentConflict>();

            DetectLaneChange(accidentId, d1, d2, conflicts);
            DetectEnteringRoad(accidentId, d1, d2, conflicts);
            DetectSpecialMove(accidentId, d1, d2, conflicts);
            DetectPosition(accidentId, d1, d2, conflicts);
            DetectIntersection(accidentId, d1, d2, conflicts);
            DetectOvertake(accidentId, d1, d2, conflicts);

            var oldConflicts = await _db.AccidentConflicts
                .Where(c => c.AccidentId == accidentId)
                .ToListAsync();

            if (oldConflicts.Any())
                _db.AccidentConflicts.RemoveRange(oldConflicts);

            if (conflicts.Any())
                _db.AccidentConflicts.AddRange(conflicts);

            await _db.SaveChangesAsync();
        }

        // =====================================================
        // Load driver answers
        // =====================================================
        private async Task<Dictionary<string, string>> LoadAnswers(int accidentId, int driverUserId)
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

        // =====================================================
        // Lane Change
        // CQ1 vs M1 (both directions)
        // =====================================================
        private void DetectLaneChange(int accidentId,
            Dictionary<string, string> d1,
            Dictionary<string, string> d2,
            List<AccidentConflict> conflicts)
        {
            bool? d1Claim = MapCq1(d1.GetValueOrDefault("CQ1"));
            bool? d2ObsAboutD1 = MapM1(d2.GetValueOrDefault("M1"));

            if (IsDirectConflict(d1Claim, d2ObsAboutD1))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.LaneChange,
                    Severity = ConflictSeverity.Critical,
                    Summary = "Lane change contradiction between Driver 1 claim and Driver 2 observation."
                });
                return;
            }

            bool? d2Claim = MapCq1(d2.GetValueOrDefault("CQ1"));
            bool? d1ObsAboutD2 = MapM1(d1.GetValueOrDefault("M1"));

            if (IsDirectConflict(d2Claim, d1ObsAboutD2))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.LaneChange,
                    Severity = ConflictSeverity.Critical,
                    Summary = "Lane change contradiction between Driver 2 claim and Driver 1 observation."
                });
            }
        }

        // =====================================================
        // Entering Road
        // CQ2 vs M2 (both directions)
        // =====================================================
        private void DetectEnteringRoad(int accidentId,
            Dictionary<string, string> d1,
            Dictionary<string, string> d2,
            List<AccidentConflict> conflicts)
        {
            bool? d1Claim = MapYesNo(d1.GetValueOrDefault("CQ2"));
            bool? d2ObsAboutD1 = MapYesNo(d2.GetValueOrDefault("M2"));

            if (IsDirectConflict(d1Claim, d2ObsAboutD1))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.EnteringRoad,
                    Severity = ConflictSeverity.Critical,
                    Summary = "Entering main road contradiction between Driver 1 claim and Driver 2 observation."
                });
                return;
            }

            bool? d2Claim = MapYesNo(d2.GetValueOrDefault("CQ2"));
            bool? d1ObsAboutD2 = MapYesNo(d1.GetValueOrDefault("M2"));

            if (IsDirectConflict(d2Claim, d1ObsAboutD2))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.EnteringRoad,
                    Severity = ConflictSeverity.Critical,
                    Summary = "Entering main road contradiction between Driver 2 claim and Driver 1 observation."
                });
            }
        }

        // =====================================================
        // Special Move
        // CQ3 vs M3 (both directions)
        // =====================================================
        private void DetectSpecialMove(int accidentId,
            Dictionary<string, string> d1,
            Dictionary<string, string> d2,
            List<AccidentConflict> conflicts)
        {
            bool? d1Claim = MapSpecialMove(d1.GetValueOrDefault("CQ3"));
            bool? d2ObsAboutD1 = MapYesNo(d2.GetValueOrDefault("M3"));

            if (IsDirectConflict(d1Claim, d2ObsAboutD1))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.SpecialMove,
                    Severity = ConflictSeverity.High,
                    Summary = "Special move contradiction between Driver 1 claim and Driver 2 observation."
                });
                return;
            }

            bool? d2Claim = MapSpecialMove(d2.GetValueOrDefault("CQ3"));
            bool? d1ObsAboutD2 = MapYesNo(d1.GetValueOrDefault("M3"));

            if (IsDirectConflict(d2Claim, d1ObsAboutD2))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.SpecialMove,
                    Severity = ConflictSeverity.High,
                    Summary = "Special move contradiction between Driver 2 claim and Driver 1 observation."
                });
            }
        }

        // =====================================================
        // Position conflict
        // CQ5 vs CQ5
        // =====================================================
        private void DetectPosition(int accidentId,
            Dictionary<string, string> d1,
            Dictionary<string, string> d2,
            List<AccidentConflict> conflicts)
        {
            var a = d1.GetValueOrDefault("CQ5");
            var b = d2.GetValueOrDefault("CQ5");

            if ((a == "CQ5_BEHIND" && b == "CQ5_BEHIND") ||
                (a == "CQ5_AHEAD" && b == "CQ5_AHEAD"))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.Position,
                    Severity = ConflictSeverity.Medium,
                    Summary = "Impossible relative position reported by both drivers."
                });
            }
        }

        // =====================================================
        // Intersection conflicts
        // CQ7 / CQ8 / CQ9
        // =====================================================
        private void DetectIntersection(int accidentId,
            Dictionary<string, string> d1,
            Dictionary<string, string> d2,
            List<AccidentConflict> conflicts)
        {
            var cq7a = d1.GetValueOrDefault("CQ7");
            var cq7b = d2.GetValueOrDefault("CQ7");

            if (!string.IsNullOrWhiteSpace(cq7a) && !string.IsNullOrWhiteSpace(cq7b))
            {
                if ((cq7a == "CQ7_LIGHT" && cq7b == "CQ7_NONE") ||
                    (cq7b == "CQ7_LIGHT" && cq7a == "CQ7_NONE"))
                {
                    conflicts.Add(new AccidentConflict
                    {
                        AccidentId = accidentId,
                        ConflictType = ConflictType.IntersectionControl,
                        Severity = ConflictSeverity.Medium,
                        Summary = "Mismatch in intersection control."
                    });
                }
            }

            var cq8a = d1.GetValueOrDefault("CQ8");
            var cq8b = d2.GetValueOrDefault("CQ8");

            if (!string.IsNullOrWhiteSpace(cq8a) && !string.IsNullOrWhiteSpace(cq8b))
            {
                if ((cq8a == "CQ8_YES" && cq8b == "CQ8_NO") ||
                    (cq8a == "CQ8_NO" && cq8b == "CQ8_YES"))
                {
                    conflicts.Add(new AccidentConflict
                    {
                        AccidentId = accidentId,
                        ConflictType = ConflictType.IntersectionCompliance,
                        Severity = ConflictSeverity.High,
                        Summary = "Intersection compliance contradiction."
                    });
                }
            }

            var cq9a = d1.GetValueOrDefault("CQ9");
            var cq9b = d2.GetValueOrDefault("CQ9");

            if (!string.IsNullOrWhiteSpace(cq9a) && !string.IsNullOrWhiteSpace(cq9b))
            {
                if ((cq9a == "CQ9_ME" && cq9b == "CQ9_ME") ||
                    (cq9a == "CQ9_OTHER" && cq9b == "CQ9_OTHER"))
                {
                    conflicts.Add(new AccidentConflict
                    {
                        AccidentId = accidentId,
                        ConflictType = ConflictType.IntersectionEntryFirst,
                        Severity = ConflictSeverity.Medium,
                        Summary = "Both drivers claim same entry priority."
                    });
                }
            }
        }

        // =====================================================
        // Overtake
        // CQ10 vs M5 (both directions)
        // =====================================================
        private void DetectOvertake(int accidentId,
            Dictionary<string, string> d1,
            Dictionary<string, string> d2,
            List<AccidentConflict> conflicts)
        {
            bool? d1Claim = MapYesNo(d1.GetValueOrDefault("CQ10"));
            bool? d2ObsAboutD1 = MapYesNo(d2.GetValueOrDefault("M5"));

            if (IsDirectConflict(d1Claim, d2ObsAboutD1))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.Overtake,
                    Severity = ConflictSeverity.High,
                    Summary = "Overtaking contradiction between Driver 1 claim and Driver 2 observation."
                });
                return;
            }

            bool? d2Claim = MapYesNo(d2.GetValueOrDefault("CQ10"));
            bool? d1ObsAboutD2 = MapYesNo(d1.GetValueOrDefault("M5"));

            if (IsDirectConflict(d2Claim, d1ObsAboutD2))
            {
                conflicts.Add(new AccidentConflict
                {
                    AccidentId = accidentId,
                    ConflictType = ConflictType.Overtake,
                    Severity = ConflictSeverity.High,
                    Summary = "Overtaking contradiction between Driver 2 claim and Driver 1 observation."
                });
            }
        }

        // =====================================================
        // Mapping helpers
        // =====================================================
        private bool? MapCq1(string? code)
        {
            if (code == "CQ1_LEFT" || code == "CQ1_RIGHT") return true;
            if (code == "CQ1_NO") return false;
            return null;
        }

        private bool? MapYesNo(string? code)
        {
            if (code == null) return null;

            if (code.EndsWith("_YES")) return true;
            if (code.EndsWith("_NO")) return false;

            return null;
        }

        private bool? MapM1(string? code)
        {
            if (code == "M1_YES") return true;
            if (code == "M1_NO") return false;
            return null;
        }

        private bool? MapSpecialMove(string? code)
        {
            if (code == "CQ3_REVERSING") return true;

            if (code == "CQ3_UTURN") return true;

            if (code == "CQ3_NORMAL" || code == "CQ3_SLOW") return false;

            return null;
        }

        private bool IsDirectConflict(bool? a, bool? b)
        {
            return a.HasValue && b.HasValue && a.Value != b.Value;
        }
    }
}