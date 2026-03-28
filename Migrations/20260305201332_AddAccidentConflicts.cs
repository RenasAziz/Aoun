using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aoun.Migrations
{
    /// <inheritdoc />
    public partial class AddAccidentConflicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accident",
                columns: table => new
                {
                    accident_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    accident_date = table.Column<DateOnly>(type: "date", nullable: true),
                    accident_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    accident_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Accident__A27CA62BCAA43B20", x => x.accident_id);
                });

            migrationBuilder.CreateTable(
                name: "Driver",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    driver_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    license_number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Driver__...", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "Question",
                columns: table => new
                {
                    question_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    question_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    question_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    question_text_ar = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    question_text = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Question__2EC215495087E83F", x => x.question_id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    password = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    phone_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicle",
                columns: table => new
                {
                    vehicle_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    license_plate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    model = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    year = table.Column<int>(type: "int", nullable: true),
                    color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    driver_user_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Vehicle__...", x => x.vehicle_id);
                });

            migrationBuilder.CreateTable(
                name: "Accident_Report",
                columns: table => new
                {
                    report_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    fault_percent_driver1 = table.Column<int>(type: "int", nullable: true),
                    fault_percent_driver2 = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
                    approval_status = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    pdf_path = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    summary = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    accident_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Accident__779B7C58890D5B1E", x => x.report_id);
                    table.ForeignKey(
                        name: "FK_Report_Accident",
                        column: x => x.accident_id,
                        principalTable: "Accident",
                        principalColumn: "accident_id");
                });

            migrationBuilder.CreateTable(
                name: "AccidentConflicts",
                columns: table => new
                {
                    AccidentConflictId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccidentId = table.Column<int>(type: "int", nullable: false),
                    ConflictType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccidentConflicts", x => x.AccidentConflictId);
                    table.ForeignKey(
                        name: "FK_AccidentConflicts_Accident_AccidentId",
                        column: x => x.AccidentId,
                        principalTable: "Accident",
                        principalColumn: "accident_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccidentSessionParticipants",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    accident_id = table.Column<int>(type: "int", nullable: false),
                    driver_user_id = table.Column<int>(type: "int", nullable: false),
                    role = table.Column<byte>(type: "tinyint", nullable: false),
                    is_joined = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    joined_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    vehicle_id = table.Column<int>(type: "int", nullable: true),
                    current_step = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Waiting"),
                    is_completed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccidentSessionParticipants", x => x.id);
                    table.ForeignKey(
                        name: "FK_AccidentSessionParticipants_Accident_accident_id",
                        column: x => x.accident_id,
                        principalTable: "Accident",
                        principalColumn: "accident_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Driver_Feedback",
                columns: table => new
                {
                    accident_id = table.Column<int>(type: "int", nullable: false),
                    driver_user_id = table.Column<int>(type: "int", nullable: false),
                    satisfaction_level = table.Column<int>(type: "int", nullable: true),
                    comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    feedback_date = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Driver_Feedback", x => new { x.accident_id, x.driver_user_id });
                    table.ForeignKey(
                        name: "FK_Feedback_Accident",
                        column: x => x.accident_id,
                        principalTable: "Accident",
                        principalColumn: "accident_id");
                    table.ForeignKey(
                        name: "FK_Feedback_Driver",
                        column: x => x.driver_user_id,
                        principalTable: "Driver",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    accident_id = table.Column<int>(type: "int", nullable: false),
                    driver_user_id = table.Column<int>(type: "int", nullable: false),
                    report_time = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => new { x.accident_id, x.driver_user_id });
                    table.ForeignKey(
                        name: "FK_Reports_Accident",
                        column: x => x.accident_id,
                        principalTable: "Accident",
                        principalColumn: "accident_id");
                    table.ForeignKey(
                        name: "FK_Reports_Driver",
                        column: x => x.driver_user_id,
                        principalTable: "Driver",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "Answer",
                columns: table => new
                {
                    accident_id = table.Column<int>(type: "int", nullable: false),
                    driver_user_id = table.Column<int>(type: "int", nullable: false),
                    question_id = table.Column<int>(type: "int", nullable: false),
                    answered_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(sysdatetime())"),
                    selected_option_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    free_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    response = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Answer", x => new { x.accident_id, x.driver_user_id, x.question_id });
                    table.ForeignKey(
                        name: "FK_Answer_Accident",
                        column: x => x.accident_id,
                        principalTable: "Accident",
                        principalColumn: "accident_id");
                    table.ForeignKey(
                        name: "FK_Answer_Question",
                        column: x => x.question_id,
                        principalTable: "Question",
                        principalColumn: "question_id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionOption",
                columns: table => new
                {
                    option_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    question_id = table.Column<int>(type: "int", nullable: false),
                    option_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    option_text_ar = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionOption", x => x.option_id);
                    table.ForeignKey(
                        name: "FK_QuestionOption_Question",
                        column: x => x.question_id,
                        principalTable: "Question",
                        principalColumn: "question_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Image",
                columns: table => new
                {
                    accident_id = table.Column<int>(type: "int", nullable: false),
                    image_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    image_path = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    upload_date = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
                    label = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    driver_user_id = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Image", x => new { x.accident_id, x.image_id });
                    table.ForeignKey(
                        name: "FK_Image_Accident",
                        column: x => x.accident_id,
                        principalTable: "Accident",
                        principalColumn: "accident_id");
                    table.ForeignKey(
                        name: "FK_Image_Users_driver_user_id",
                        column: x => x.driver_user_id,
                        principalTable: "Users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "Involves",
                columns: table => new
                {
                    accident_id = table.Column<int>(type: "int", nullable: false),
                    vehicle_id = table.Column<int>(type: "int", nullable: false),
                    vehicle_role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Involves", x => new { x.accident_id, x.vehicle_id });
                    table.ForeignKey(
                        name: "FK_Involves_Accident",
                        column: x => x.accident_id,
                        principalTable: "Accident",
                        principalColumn: "accident_id");
                    table.ForeignKey(
                        name: "FK_Involves_Vehicle",
                        column: x => x.vehicle_id,
                        principalTable: "Vehicle",
                        principalColumn: "vehicle_id");
                });

            migrationBuilder.CreateIndex(
                name: "UQ__Accident__A27CA62AE269DDD4",
                table: "Accident_Report",
                column: "accident_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccidentConflicts_AccidentId_ConflictType",
                table: "AccidentConflicts",
                columns: new[] { "AccidentId", "ConflictType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccidentSessionParticipants_accident_id",
                table: "AccidentSessionParticipants",
                column: "accident_id");

            migrationBuilder.CreateIndex(
                name: "IX_Answer_question_id",
                table: "Answer",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "UQ__Driver__D482A0036CD618F0",
                table: "Driver",
                column: "license_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Driver_Feedback_driver_user_id",
                table: "Driver_Feedback",
                column: "driver_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_Image_driver_user_id",
                table: "Image",
                column: "driver_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_Involves_vehicle_id",
                table: "Involves",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOption_question_id",
                table: "QuestionOption",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_driver_user_id",
                table: "Reports",
                column: "driver_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_Users_email",
                table: "Users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__Vehicle__F72CD56EA546ED93",
                table: "Vehicle",
                column: "license_plate",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accident_Report");

            migrationBuilder.DropTable(
                name: "AccidentConflicts");

            migrationBuilder.DropTable(
                name: "AccidentSessionParticipants");

            migrationBuilder.DropTable(
                name: "Answer");

            migrationBuilder.DropTable(
                name: "Driver_Feedback");

            migrationBuilder.DropTable(
                name: "Image");

            migrationBuilder.DropTable(
                name: "Involves");

            migrationBuilder.DropTable(
                name: "QuestionOption");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Vehicle");

            migrationBuilder.DropTable(
                name: "Question");

            migrationBuilder.DropTable(
                name: "Accident");

            migrationBuilder.DropTable(
                name: "Driver");
        }
    }
}
