using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bravo.Video.Lectores;
using Bravo.Video.Patentes;

namespace Bravo.Video.Conector;

/// <summary>
/// Configuración del conector del local.
///
/// Lo que describe la instalación (sede, cámaras, roles) va en un archivo JSON
/// (<c>VIDEO_CONECTOR_CONFIG</c>, por defecto <c>conector.json</c>). Los secretos
/// van solo en variables de entorno: <c>VIDEO_CONECTOR_TOKEN</c> (obligatorio) y
/// <c>VIDEO_MQTT_USUARIO</c> / <c>VIDEO_MQTT_CLAVE</c> (si Mosquitto pide usuario).
/// </summary>
public sealed record ConfiguracionConector
{
    public const string VarArchivo = "VIDEO_CONECTOR_CONFIG";
    public const string VarToken = "VIDEO_CONECTOR_TOKEN";
    public const string VarMqttUsuario = "VIDEO_MQTT_USUARIO";
    public const string VarMqttClave = "VIDEO_MQTT_CLAVE";

    public required ConfiguracionSede Sede { get; init; }
    public required Uri UrlIngesta { get; init; }
    public required string Token { get; init; }
    public required string DirectorioCola { get; init; }
    public required int MaximoCola { get; init; }
    public ConfiguracionMqtt? Mqtt { get; init; }

    /// <summary>Cámaras ANPR y sensores que envían por HTTP, con la IP desde la que se les acepta.</summary>
    public required IReadOnlyDictionary<string, OrigenHttp> CamarasHttp { get; init; }

    public required IReadOnlyDictionary<string, OrigenHttp> Sensores { get; init; }

    /// <summary>
    /// Una cámara ANPR o un sensor solo mandan algo cuando pasa un auto o una
    /// persona. Si no mandaron nada en esta ventana, el latido los informa como
    /// "desconocido", no como caídos: de noche el silencio es normal.
    /// </summary>
    public required TimeSpan VentanaActividad { get; init; }

    public static ConfiguracionConector Leer(IConfiguration entorno)
    {
        var ruta = entorno[VarArchivo] ?? "conector.json";
        if (!File.Exists(ruta))
        {
            throw new InvalidOperationException($"No existe el archivo de configuración '{ruta}' ({VarArchivo}).");
        }

        ArchivoConfiguracion archivo;
        try
        {
            archivo = JsonSerializer.Deserialize<ArchivoConfiguracion>(File.ReadAllText(ruta), OpcionesArchivo)
                ?? throw new InvalidOperationException("Archivo de configuración vacío.");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"Archivo de configuración inválido: {e.Message}", e);
        }

        return Armar(archivo, entorno);
    }

    internal static readonly JsonSerializerOptions OpcionesArchivo = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static ConfiguracionConector Armar(ArchivoConfiguracion a, IConfiguration entorno)
    {
        var errores = new List<string>();

        void Requerido(string? valor, string campo)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                errores.Add($"Falta '{campo}'.");
            }
        }

        Requerido(a.Sede, "sede");
        Requerido(a.Dispositivo, "dispositivo");
        Requerido(a.DirectorioCola, "directorioCola");

        if (!Enum.TryParse<Pais>(a.Pais, out var pais))
        {
            errores.Add("'pais' tiene que ser AR o MX.");
        }

        TimeZoneInfo? zona = null;
        try
        {
            zona = TimeZoneInfo.FindSystemTimeZoneById(a.ZonaHoraria ?? string.Empty);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            errores.Add("'zonaHoraria' no es una zona IANA válida (ej. America/Argentina/Buenos_Aires).");
        }

        if (!Uri.TryCreate(a.UrlIngesta, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback))
        {
            errores.Add("'urlIngesta' tiene que ser una URL https (http solo hacia localhost, para pruebas).");
        }

        var token = entorno[VarToken];
        if (string.IsNullOrWhiteSpace(token))
        {
            errores.Add($"Falta la variable de entorno {VarToken}.");
        }

        if (a.MaximoCola < 1)
        {
            errores.Add("'maximoCola' tiene que ser mayor que cero.");
        }

        var camaras = new Dictionary<string, ConfiguracionCamara>(StringComparer.Ordinal);
        var camarasHttp = new Dictionary<string, OrigenHttp>(StringComparer.Ordinal);
        foreach (var c in a.Camaras ?? [])
        {
            if (string.IsNullOrWhiteSpace(c.Camara) || !camaras.TryAdd(c.Camara, c.AConfiguracion()))
            {
                errores.Add($"Cámara sin nombre o repetida: '{c.Camara}'.");
                continue;
            }

            switch (c.Rol)
            {
                case RolCamara.PorZona when string.IsNullOrWhiteSpace(c.ZonaIngreso) || string.IsNullOrWhiteSpace(c.ZonaEgreso):
                    errores.Add($"Cámara '{c.Camara}': PorZona necesita zonaIngreso y zonaEgreso.");
                    break;
                case RolCamara.PorSentido when string.IsNullOrWhiteSpace(c.SentidoIngreso) || string.IsNullOrWhiteSpace(c.SentidoEgreso):
                    errores.Add($"Cámara '{c.Camara}': PorSentido necesita sentidoIngreso y sentidoEgreso.");
                    break;
            }

            if (c.Motor == Motores.HikvisionAnpr)
            {
                if (Origen(c.Ip, c.Firmware) is { } origen)
                {
                    camarasHttp[c.Camara] = origen;
                }
                else
                {
                    errores.Add($"Cámara '{c.Camara}': una cámara ANPR necesita 'ip' válida y 'firmware'.");
                }
            }
            else if (c.Motor != Motores.Frigate)
            {
                errores.Add($"Cámara '{c.Camara}': motor '{c.Motor}' desconocido (frigate o hikvision-anpr).");
            }
        }

        var sensores = new Dictionary<string, OrigenHttp>(StringComparer.Ordinal);
        foreach (var s in a.Sensores ?? [])
        {
            if (string.IsNullOrWhiteSpace(s.Sensor) || Origen(s.Ip, s.Firmware) is not { } origen || !sensores.TryAdd(s.Sensor, origen))
            {
                errores.Add($"Sensor '{s.Sensor}': necesita nombre único, 'ip' válida y 'firmware'.");
            }
        }

        if (a.Mqtt is { } mqtt)
        {
            Requerido(mqtt.Host, "mqtt.host");
            Requerido(mqtt.VersionFrigate, "mqtt.versionFrigate");
        }

        if (errores.Count > 0)
        {
            throw new InvalidOperationException("Configuración del conector incompleta: " + string.Join(" ", errores));
        }

        return new ConfiguracionConector
        {
            Sede = new ConfiguracionSede
            {
                Sede = a.Sede!, Dispositivo = a.Dispositivo!, Pais = pais, ZonaHoraria = zona!, Camaras = camaras,
            },
            UrlIngesta = url!,
            Token = token!,
            DirectorioCola = a.DirectorioCola!,
            MaximoCola = a.MaximoCola,
            Mqtt = a.Mqtt is { } m
                ? m with { Usuario = entorno[VarMqttUsuario], Clave = entorno[VarMqttClave] }
                : null,
            CamarasHttp = camarasHttp,
            Sensores = sensores,
            VentanaActividad = TimeSpan.FromMinutes(a.VentanaActividadMinutos),
        };
    }

    private static OrigenHttp? Origen(string? ip, string? firmware) =>
        IPAddress.TryParse(ip, out var direccion) && !string.IsNullOrWhiteSpace(firmware)
            ? new OrigenHttp(direccion, firmware)
            : null;
}

/// <summary>Equipo que envía por HTTP y desde qué IP se le acepta.</summary>
public sealed record OrigenHttp(IPAddress Ip, string Firmware)
{
    public bool Acepta(IPAddress? remota) =>
        remota is not null && (remota.IsIPv4MappedToIPv6 ? remota.MapToIPv4() : remota).Equals(Ip);
}

public sealed record ConfiguracionMqtt
{
    public required string Host { get; init; }
    public int Puerto { get; init; } = 1883;
    public required string VersionFrigate { get; init; }

    [JsonIgnore]
    public string? Usuario { get; init; }

    [JsonIgnore]
    public string? Clave { get; init; }
}

/// <summary>Forma del archivo <c>conector.json</c>.</summary>
internal sealed record ArchivoConfiguracion
{
    public string? Sede { get; init; }
    public string? Dispositivo { get; init; }
    public string? Pais { get; init; }
    public string? ZonaHoraria { get; init; }
    public string? UrlIngesta { get; init; }
    public string? DirectorioCola { get; init; }
    public int MaximoCola { get; init; } = 200_000;
    public int VentanaActividadMinutos { get; init; } = 60;
    public ConfiguracionMqtt? Mqtt { get; init; }
    public List<CamaraArchivo>? Camaras { get; init; }
    public List<SensorArchivo>? Sensores { get; init; }
}

internal sealed record CamaraArchivo
{
    public string? Camara { get; init; }
    public string Motor { get; init; } = Motores.Frigate;
    public RolCamara Rol { get; init; }
    public string? ZonaIngreso { get; init; }
    public string? ZonaEgreso { get; init; }
    public string? SentidoIngreso { get; init; }
    public string? SentidoEgreso { get; init; }
    public string? Ip { get; init; }
    public string? Firmware { get; init; }

    public ConfiguracionCamara AConfiguracion() => new()
    {
        Camara = Camara!, Rol = Rol, ZonaIngreso = ZonaIngreso, ZonaEgreso = ZonaEgreso,
        SentidoIngreso = SentidoIngreso, SentidoEgreso = SentidoEgreso,
    };
}

internal sealed record SensorArchivo
{
    public string? Sensor { get; init; }
    public string? Ip { get; init; }
    public string? Firmware { get; init; }
}
