using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bravo.Video.Ingesta.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Equipos",
                columns: table => new
                {
                    Dispositivo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Sede = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoContactoUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipos", x => x.Dispositivo);
                });

            migrationBuilder.CreateTable(
                name: "Eventos",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Dispositivo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Sede = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Camara = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TsUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TsOffsetMinutos = table.Column<int>(type: "int", nullable: false),
                    RecibidoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Motor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VersionMotor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdOrigen = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PatentePais = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PatenteLeida = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    PatenteNormalizada = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    PatenteHash = table.Column<string>(type: "nvarchar(67)", maxLength: 67, nullable: true),
                    PatenteValida = table.Column<bool>(type: "bit", nullable: true),
                    PatenteFormato = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PatenteCorrecciones = table.Column<int>(type: "int", nullable: true),
                    PatenteConfianza = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    PatenteAnonimizadaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Linea = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Entradas = table.Column<int>(type: "int", nullable: true),
                    Salidas = table.Column<int>(type: "int", nullable: true),
                    DesdeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HastaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Box = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    VersionConector = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ColaPendiente = table.Column<int>(type: "int", nullable: true),
                    DesvioRelojMs = table.Column<long>(type: "bigint", nullable: true),
                    MotoresJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eventos", x => new { x.Dispositivo, x.EventId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Equipos_TokenHash",
                table: "Equipos",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_Dispositivo_TsUtc",
                table: "Eventos",
                columns: new[] { "Dispositivo", "TsUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_PatenteAnonimizadaUtc_TsUtc",
                table: "Eventos",
                columns: new[] { "PatenteAnonimizadaUtc", "TsUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_PatenteHash",
                table: "Eventos",
                column: "PatenteHash");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_Sede_TsUtc",
                table: "Eventos",
                columns: new[] { "Sede", "TsUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Equipos");

            migrationBuilder.DropTable(
                name: "Eventos");
        }
    }
}
