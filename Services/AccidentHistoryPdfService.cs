using Aoun.ViewModels;
using Aoun.ViewModels.Accident;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Aoun.Services
{
    public class AccidentHistoryPdfService
    {
        private readonly IWebHostEnvironment _env;

        public AccidentHistoryPdfService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public byte[] Generate(AccidentHistoryDetailsViewModel model)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.PageColor(Colors.White);

                    page.DefaultTextStyle(x => x
                        .FontSize(11)
                        .FontFamily("Arial"));

                    page.ContentFromRightToLeft();

                    page.Content().Column(column =>
                    {
                        column.Spacing(14);

                        column.Item().Element(c => ComposeFirstPageHeader(c, model));
                        column.Item().Element(c => ComposeReportMeta(c, model));
                        column.Item().Element(c => ComposeAccidentSection(c, model));
                        column.Item().Element(c => ComposeDecisionSection(c, model));
                        column.Item().Element(c => ComposeVehicleSection(c, model));
                        column.Item().Element(c => ComposeInspectorNoteSection(c, model));

                        if (model.HasConflicts)
                            column.Item().Element(c => ComposeConflictAlert(c));

                        if (model.HasDamageImages)
                            column.Item().Element(c => ComposeDamageSection(c, model));

                        column.Item().Element(c => ComposeApprovalSection(c, model));
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken1));
                        text.Span("تقرير حادث رسمي");
                        text.Span("  |  ");
                        text.Span("هذا المستند للاستخدام المرجعي فقط");
                    });
                });
            }).GeneratePdf();
        }

        private void ComposeFirstPageHeader(IContainer container, AccidentHistoryDetailsViewModel model)
        {
            container.Column(column =>
            {
                column.Spacing(10);

                column.Item().Row(row =>
                {
                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Spacing(4);

                        var logoPath = Path.Combine(_env.WebRootPath, "images", "logo.png");
                        if (File.Exists(logoPath))
                        {
                            col.Item().AlignRight().Width(90).Image(File.ReadAllBytes(logoPath), ImageScaling.FitWidth);
                        }

                        col.Item().AlignRight().Text(model.ReportTitle)
                            .Bold()
                            .FontSize(18)
                            .FontColor("#111827");

                        col.Item().AlignRight().Text($"رقم الحادث: {model.AccidentCode}")
                            .SemiBold()
                            .FontSize(11)
                            .FontColor(Colors.Grey.Darken2);
                    });
                });

                column.Item().PaddingTop(4).LineHorizontal(1).LineColor("#D1D5DB");
            });
        }

        private void ComposeReportMeta(IContainer container, AccidentHistoryDetailsViewModel model)
        {
            ComposeSectionCard(container, "بيانات التقرير", body =>
            {
                body.Column(col =>
                {
                    col.Spacing(7);
                    col.Item().Element(x => ComposeInfoRow(x, "مصدر التقرير", model.ReportSource));
                    col.Item().Element(x => ComposeInfoRow(x, "المرجع", model.ReportReference));
                    col.Item().Element(x => ComposeInfoRow(x, "تاريخ الإنشاء", model.GeneratedOnText));
                    col.Item().Element(x => ComposeInfoRow(x, "حالة التقرير", model.ApprovalStatus));
                });
            });
        }

        private void ComposeAccidentSection(IContainer container, AccidentHistoryDetailsViewModel model)
        {
            ComposeSectionCard(container, "بيانات الحادث", body =>
            {
                body.Column(col =>
                {
                    col.Spacing(7);
                    col.Item().Element(x => ComposeInfoRow(x, "التاريخ", model.FormattedDate));
                    col.Item().Element(x => ComposeInfoRow(x, "الوقت", model.FormattedTime));
                    col.Item().Element(x => ComposeInfoRow(x, "الموقع", model.Location));
                    col.Item().Element(x => ComposeInfoRow(x, "السائق", model.DriverName));
                    col.Item().Element(x => ComposeInfoRow(x, "صفة السائق", model.DriverRoleText));
                });
            });
        }

        private void ComposeDecisionSection(IContainer container, AccidentHistoryDetailsViewModel model)
        {
            ComposeSectionCard(container, "النتيجة والقرار", body =>
            {
                body.Column(col =>
                {
                    col.Spacing(8);

                    col.Item().Element(x => ComposeInfoRow(x, "نوع الحادث المحدد", model.AccidentClassification));
                    col.Item().Element(x => ComposeInfoRow(x, "القاعدة المطبقة", model.RuleId));
                    col.Item().Element(x => ComposeInfoRow(x, "مستوى الثقة", model.ConfidencePercentText));
                    col.Item().Element(x => ComposeInfoRow(x, model.OtherDriverLabel, $"{model.OtherDriverFaultPercent}%"));
                    col.Item().Element(x => ComposeInfoRow(x, model.CurrentDriverLabel, $"{model.CurrentDriverFaultPercent}%"));

                    col.Item().PaddingTop(6).Text("مبررات القرار")
                        .Bold()
                        .FontColor("#14532D");

                    col.Item()
                        .Background("#F9FAFB")
                        .Border(1)
                        .BorderColor("#D1D5DB")
                        .Padding(10)
                        .CornerRadius(6)
                        .Text(string.IsNullOrWhiteSpace(model.DecisionExplanation) ? "—" : model.DecisionExplanation);
                });
            });
        }

        private void ComposeVehicleSection(IContainer container, AccidentHistoryDetailsViewModel model)
        {
            ComposeSectionCard(container, "بيانات المركبة المشاركة في الحادث", body =>
            {
                body.Column(col =>
                {
                    col.Spacing(7);
                    col.Item().Element(x => ComposeInfoRow(x, "رقم اللوحة", model.VehiclePlate));
                    col.Item().Element(x => ComposeInfoRow(x, "الموديل", model.VehicleModel));
                    col.Item().Element(x => ComposeInfoRow(x, "اللون", model.VehicleColor));
                    col.Item().Element(x => ComposeInfoRow(x, "سنة الصنع", model.VehicleYearText));
                });
            });
        }

        private void ComposeInspectorNoteSection(IContainer container, AccidentHistoryDetailsViewModel model)
        {
            ComposeSectionCard(container, "ملاحظة المحقق", body =>
            {
                body
                    .Background("#F8FAFC")
                    .Border(1)
                    .BorderColor("#CBD5E1")
                    .Padding(12)
                    .CornerRadius(6)
                    .Text(string.IsNullOrWhiteSpace(model.InspectorNote)
                        ? "لا توجد ملاحظة من المحقق على هذا الحادث."
                        : model.InspectorNote);
            });
        }

        private void ComposeConflictAlert(IContainer container)
        {
            ComposeSectionCard(container, "تنبيه", body =>
            {
                body
                    .Background("#FFF7ED")
                    .Border(1)
                    .BorderColor("#FDBA74")
                    .Padding(10)
                    .CornerRadius(6)
                    .Text("تم رصد تناقض بين إجابات الطرفين، وقد يؤثر ذلك على دقة التقييم الأولي.");
            });
        }

        private void ComposeDamageSection(IContainer container, AccidentHistoryDetailsViewModel model)
        {
            ComposeSectionCard(container, "نتائج صور الضرر", body =>
            {
                body.Column(col =>
                {
                    col.Spacing(14);

                    ComposeDamageBlock(col, "صورة الضرر الأولى",
                        model.Damage1PredictedLabel,
                        model.Damage1PredictionConfidence,
                        model.Damage1SegmentationResultPath,
                        model.Damage1SegmentationHasDamage,
                        model.Damage1SegmentationDetections);

                    ComposeDamageBlock(col, "صورة الضرر الثانية",
                        model.Damage2PredictedLabel,
                        model.Damage2PredictionConfidence,
                        model.Damage2SegmentationResultPath,
                        model.Damage2SegmentationHasDamage,
                        model.Damage2SegmentationDetections);
                });
            });
        }

        private void ComposeDamageBlock(
            ColumnDescriptor col,
            string title,
            string? predictedLabel,
            double? predictionConfidence,
            string? resultPath,
            bool? hasDamage,
            List<SegmentationDetectionDisplayItem>? detections)
        {
            var hasAnyContent =
                !string.IsNullOrWhiteSpace(predictedLabel) ||
                !string.IsNullOrWhiteSpace(resultPath) ||
                (detections != null && detections.Any()) ||
                hasDamage == false;

            if (!hasAnyContent)
                return;

            col.Item()
                .Background("#F9FAFB")
                .Border(1)
                .BorderColor("#E5E7EB")
                .Padding(10)
                .CornerRadius(6)
                .Column(inner =>
                {
                    inner.Spacing(8);

                    inner.Item().Text(title)
                        .Bold()
                        .FontColor("#14532D");

                    if (!string.IsNullOrWhiteSpace(predictedLabel))
                    {
                        inner.Item().Element(x => ComposeInfoRow(
                            x,
                            "تصنيف جهة الضرر",
                            $"{GetArabicLabel(predictedLabel)} - نسبة الثقة: {predictionConfidence:0.##}%"));
                    }

                    if (!string.IsNullOrWhiteSpace(resultPath))
                    {
                        var physicalPath = ToPhysicalPath(resultPath);
                        if (File.Exists(physicalPath))
                        {
                            inner.Item().Text("صورة تحديد مناطق الضرر").SemiBold();
                            inner.Item().Height(210).Image(File.ReadAllBytes(physicalPath), ImageScaling.FitArea);
                        }
                    }

                    if (detections != null && detections.Any())
                    {
                        inner.Item().Text("أنواع الضرر المكتشفة").SemiBold();

                        foreach (var det in detections)
                        {
                            inner.Item().Element(x => ComposeInfoRow(
                                x,
                                GetArabicDamageLabel(det.Label),
                                $"{det.Confidence:0.##}%"));
                        }
                    }
                    else if (hasDamage == false)
                    {
                        inner.Item().Text("لم يتم رصد ضرر واضح في هذه الصورة.");
                    }
                });
        }

        private void ComposeApprovalSection(IContainer container, AccidentHistoryDetailsViewModel model)
        {
            container.PaddingTop(6).Column(col =>
            {
                col.Spacing(8);

                col.Item().LineHorizontal(1).LineColor("#D1D5DB");

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Spacing(4);
                        left.Item().Text("اعتماد التقرير").Bold().FontColor("#14532D");
                        left.Item().Text($"الحالة: {model.ApprovalStatus}");
                        left.Item().Text("تم إنشاء هذا التقرير بالاعتماد على بيانات الحادث والنتائج المسجلة في النظام.");
                    });

                    row.ConstantItem(170).Column(right =>
                    {
                        right.Spacing(18);
                        right.Item().AlignCenter().Text("توقيع / ختم").FontColor(Colors.Grey.Darken2);
                        right.Item().Height(1).Background(Colors.Grey.Medium);
                        right.Item().AlignCenter().Text(" ").FontColor(Colors.White);
                    });
                });
            });
        }

        private void ComposeSectionCard(IContainer container, string title, Action<IContainer> content)
        {
            container
                .Border(1)
                .BorderColor("#D1D5DB")
                .CornerRadius(8)
                .Padding(14)
                .Column(column =>
                {
                    column.Spacing(10);
                    column.Item().AlignRight().Text(title)
                        .Bold()
                        .FontSize(14)
                        .FontColor("#14532D");

                    column.Item().Element(content);
                });
        }

        private void ComposeInfoRow(IContainer container, string label, string value)
        {
            container.Row(row =>
            {
                row.ConstantItem(165).AlignRight().Text(label).FontColor("#4B5563");
                row.RelativeItem().AlignRight().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).SemiBold();
            });
        }

        private string ToPhysicalPath(string relativePath)
        {
            return Path.Combine(
                _env.WebRootPath,
                relativePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        }

        private string GetArabicLabel(string? label)
        {
            return label?.ToLower() switch
            {
                "front" => "أمامي",
                "back" => "خلفي",
                "side" => "جانبي",
                _ => label ?? "غير معروف"
            };
        }

        private string GetArabicDamageLabel(string? label)
        {
            return label?.ToLower() switch
            {
                "dent" => "بعج",
                "scratch" => "خدش",
                "smash" => "تهشم",
                "glass_break" => "كسر زجاج",
                _ => label ?? "غير معروف"
            };
        }
    }
}