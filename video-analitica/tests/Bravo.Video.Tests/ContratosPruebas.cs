using System.Text.Json;
using Bravo.Video.Contratos;

namespace Bravo.Video.Tests;

public class ContratosPruebas
{
    private static readonly DateTimeOffset Hora = new(2026, 11, 4, 10, 15, 30, TimeSpan.FromHours(-3));

    private static VehiculoIngreso Ingreso() => new()
    {
        EventId = IdEvento.Derivar("AR-PRUEBA-01", "minipc-01", "frigate:ingreso", "obj-1", TiposEvento.VehiculoIngreso),
        Sede = "AR-PRUEBA-01",
        Dispositivo = "minipc-01",
        Camara = "ingreso",
        Ts = Hora,
        Fuente = new Fuente("frigate", "0.17.1"),
        Patente = new LecturaPatente
        {
            Leida = "AA0O0AA", Normalizada = "AA000AA", Pais = "AR", Formato = "AR_MERCOSUR_AUTO",
            Valida = true, Correcciones = 1, Confianza = 0.93m,
        },
        IdOrigen = "obj-1",
    };

    [Fact]
    public void Ida_y_vuelta_conserva_tipo_y_datos()
    {
        var original = Ingreso();
        var json = JsonSerializer.Serialize<EventoVideo>(original, OpcionesJson.Eventos);
        var leido = JsonSerializer.Deserialize<EventoVideo>(json, OpcionesJson.Eventos);

        Assert.Equal(original, leido);
        Assert.Contains("\"tipo\":\"VEHICULO_INGRESO\"", json);
        Assert.Contains("\"ts\":\"2026-11-04T10:15:30.000-03:00\"", json);
    }

    [Fact]
    public void Acepta_tipo_que_no_viene_primero()
    {
        var json = """
        {"eventId":"0b8e4c1e-7a10-8f00-8000-000000000001","schemaVersion":"1","sede":"S","dispositivo":"D",
         "camara":"C","ts":"2026-11-04T10:00:00Z","fuente":{"motor":"m","version":"1"},
         "tipo":"BOX_LIBRE","box":"box-3"}
        """;
        var evento = JsonSerializer.Deserialize<EventoVideo>(json, OpcionesJson.Eventos);
        var box = Assert.IsType<BoxLibre>(evento);
        Assert.Equal("box-3", box.Box);
    }

    [Theory]
    [InlineData("2026-11-04T10:00:00")]
    [InlineData("2026-11-04T10:00:00.123")]
    public void Rechaza_hora_sin_zona(string ts)
    {
        var json = $$"""
        {"tipo":"BOX_LIBRE","eventId":"0b8e4c1e-7a10-8f00-8000-000000000001","sede":"S","dispositivo":"D",
         "camara":"C","ts":"{{ts}}","fuente":{"motor":"m","version":"1"},"box":"b"}
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EventoVideo>(json, OpcionesJson.Eventos));
    }

    [Fact]
    public void Rechaza_tipo_desconocido()
    {
        var json = """
        {"tipo":"OTRA_COSA","eventId":"0b8e4c1e-7a10-8f00-8000-000000000001","sede":"S","dispositivo":"D",
         "ts":"2026-11-04T10:00:00Z","fuente":{"motor":"m","version":"1"}}
        """;
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<EventoVideo>(json, OpcionesJson.Eventos));
    }

    [Fact]
    public void Rechaza_falta_de_campo_obligatorio()
    {
        var json = """
        {"tipo":"BOX_LIBRE","eventId":"0b8e4c1e-7a10-8f00-8000-000000000001","dispositivo":"D",
         "camara":"C","ts":"2026-11-04T10:00:00Z","fuente":{"motor":"m","version":"1"},"box":"b"}
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EventoVideo>(json, OpcionesJson.Eventos));
    }

    [Fact]
    public void Id_es_deterministico_y_version_8()
    {
        var a = IdEvento.Derivar("S", "D", "frigate:c", "obj-1", TiposEvento.VehiculoIngreso);
        var b = IdEvento.Derivar("S", "D", "frigate:c", "obj-1", TiposEvento.VehiculoIngreso);
        var otroTipo = IdEvento.Derivar("S", "D", "frigate:c", "obj-1", TiposEvento.VehiculoEgreso);

        Assert.Equal(a, b);
        Assert.NotEqual(a, otroTipo);
        Assert.Equal(8, a.Version);
        Assert.Equal('8', a.ToString()[14]);
    }

    [Fact]
    public void Id_no_cambia_entre_versiones()
    {
        // Si este valor cambia, un reenvío después de actualizar el conector
        // duplicaría eventos en la ingesta. No "arreglar" el valor esperado.
        var id = IdEvento.Derivar("AR-PRUEBA-01", "minipc-01", "frigate:ingreso", "1730700000.1-abc", TiposEvento.VehiculoIngreso);
        Assert.Equal(IdFijado, id.ToString());
    }

    // Calculado una vez y fijado. Ver Id_no_cambia_entre_versiones.
    private const string IdFijado = "e2828146-4489-8e97-9bd0-e1c1644e76bf";

    [Fact]
    public void Separador_no_permite_colisiones_de_concatenacion()
    {
        var a = IdEvento.Derivar("ab", "c", "m", "x", "t");
        var b = IdEvento.Derivar("a", "bc", "m", "x", "t");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Validador_acepta_evento_correcto()
    {
        Assert.Empty(ValidadorEvento.Validar(Ingreso()));
    }

    [Fact]
    public void Validador_detecta_inconsistencias()
    {
        var malo = Ingreso() with
        {
            SchemaVersion = "2",
            Sede = "",
            Patente = Ingreso().Patente! with { Valida = true, Normalizada = null, Confianza = 1.5m },
        };
        var errores = ValidadorEvento.Validar(malo);
        Assert.Contains(errores, e => e.Contains("schemaVersion"));
        Assert.Contains(errores, e => e.Contains("sede"));
        Assert.Contains(errores, e => e.Contains("patente.valida"));
        Assert.Contains(errores, e => e.Contains("confianza"));
    }

    [Fact]
    public void Latido_no_exige_camara()
    {
        var latido = new DispositivoLatido
        {
            EventId = Guid.NewGuid(), Sede = "S", Dispositivo = "D", Ts = Hora,
            Fuente = new Fuente("conector", "0.1.0"), VersionConector = "0.1.0",
            Motores = [new EstadoMotor("frigate", EstadosMotor.EnLinea)], ColaPendiente = 0,
        };
        Assert.Empty(ValidadorEvento.Validar(latido));
    }

    [Fact]
    public void Esquema_lista_los_mismos_tipos_que_el_codigo()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "esquemas", "eventos-v1.schema.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        var tipos = doc.RootElement.GetProperty("properties").GetProperty("tipo").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(TiposEvento.Todos.Order(), tipos.Order());
    }
}
