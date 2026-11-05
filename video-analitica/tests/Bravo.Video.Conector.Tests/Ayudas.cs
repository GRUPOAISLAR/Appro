using System.Net;
using Bravo.Video.Conector;
using Bravo.Video.Conector.Cola;
using Bravo.Video.Contratos;
using Bravo.Video.Lectores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravo.Video.Conector.Tests;

/// <summary>Configuración y piezas de prueba. IPs del rango de documentación (RFC 5737), patentes inventadas.</summary>
internal static class Ayudas
{
    public const string IpCamara = "192.0.2.20";
    public const string IpSensor = "192.0.2.30";

    public static ArchivoConfiguracion Archivo(string directorioCola, ConfiguracionMqtt? mqtt = null) => new()
    {
        Sede = "AR-PRUEBA-01",
        Dispositivo = "minipc-01",
        Pais = "AR",
        ZonaHoraria = "America/Argentina/Buenos_Aires",
        UrlIngesta = "http://127.0.0.1:9/",
        DirectorioCola = directorioCola,
        MaximoCola = 1000,
        Mqtt = mqtt,
        Camaras =
        [
            new CamaraArchivo { Camara = "porton", Rol = RolCamara.PorZona, ZonaIngreso = "entrada", ZonaEgreso = "salida" },
            new CamaraArchivo { Camara = "anpr-ingreso", Motor = Motores.HikvisionAnpr, Rol = RolCamara.Ingreso, Ip = IpCamara, Firmware = "V5.7.0" },
        ],
        Sensores = [new SensorArchivo { Sensor = "sensor-hall", Ip = IpSensor, Firmware = "1.0" }],
    };

    public static IConfiguration Entorno(Dictionary<string, string?>? extra = null)
    {
        var valores = new Dictionary<string, string?> { [ConfiguracionConector.VarToken] = "bvv_token-de-prueba" };
        foreach (var (k, v) in extra ?? [])
        {
            valores[k] = v;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    public static ConfiguracionConector Configuracion(string directorioCola, ConfiguracionMqtt? mqtt = null) =>
        ConfiguracionConector.Armar(Archivo(directorioCola, mqtt), Entorno());

    public static string DirectorioTemporal() =>
        Directory.CreateTempSubdirectory("video-conector-").FullName;

    public static BoxLibre Evento(int n = 0) => new()
    {
        EventId = IdEvento.Derivar("AR-PRUEBA-01", "minipc-01", "prueba", n.ToString(), TiposEvento.BoxLibre),
        Sede = "AR-PRUEBA-01",
        Dispositivo = "minipc-01",
        Camara = "box-cam",
        Ts = DateTimeOffset.UtcNow,
        Fuente = new Fuente("prueba", "1"),
        Box = $"box-{n}",
    };

    public static NullLogger<T> Log<T>() => NullLogger<T>.Instance;

    public static ColaDisco Cola(string directorio, int maximo = 1000) => new(directorio, maximo);

    public static IPAddress Ip(string ip) => IPAddress.Parse(ip);
}
