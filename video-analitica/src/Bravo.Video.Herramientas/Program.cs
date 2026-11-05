using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Bravo.Video.Evaluacion;
using Bravo.Video.Lectores;
using Bravo.Video.Patentes;
using Bravo.Video.Simulacion;
using MQTTnet;

namespace Bravo.Video.Herramientas;

/// <summary>
/// Herramientas de prueba:
///
/// <code>
/// bravo-video simular --modo seco|mqtt|http [opciones]
/// bravo-video evaluar --etiquetas etiquetas.csv --lecturas lecturas.csv [--pais AR] [--informe informe.md]
/// </code>
///
/// Los valores por defecto coinciden con <c>config/conector.ejemplo.json</c>.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "ayuda")
        {
            Console.WriteLine(Ayuda);
            return 0;
        }

        var p = Parametros(args);
        try
        {
            return args[0] switch
            {
                "simular" => await SimularAsync(p, Console.Out),
                "evaluar" => Evaluar(p, Console.Out),
                _ => Error($"Comando desconocido: {args[0]}"),
            };
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IOException or InvalidOperationException)
        {
            return Error(e.Message);
        }
    }

    public const string Ayuda = """
        bravo-video simular --modo seco|mqtt|http
            --semilla 1 --dias 1 --autos 200 --pais AR --inicio 2026-11-10
            --zona America/Argentina/Buenos_Aires --sede AR-PRUEBA-01 --dispositivo minipc-01
            --motor frigate|hikvision-anpr   (seco y mqtt: frigate; http: hikvision-anpr)
            --sin-ruido                       (todas las lecturas perfectas)
            --salida <carpeta>                (etiquetas.csv y, en modo seco, lecturas.csv)
            --mqtt-host 127.0.0.1 --mqtt-puerto 1883
            --conector http://127.0.0.1:8080
            --pausa-ms 0                      (pausa entre pasadas)

        bravo-video evaluar --etiquetas etiquetas.csv --lecturas lecturas.csv [--pais AR] [--informe informe.md]
        """;

    public static async Task<int> SimularAsync(Dictionary<string, string> p, TextWriter salida)
    {
        var modo = p.GetValueOrDefault("modo", "seco");
        var pais = Enum.Parse<Pais>(p.GetValueOrDefault("pais", "AR"), ignoreCase: true);
        var zona = TimeZoneInfo.FindSystemTimeZoneById(p.GetValueOrDefault("zona", "America/Argentina/Buenos_Aires"));
        var dia = DateTime.ParseExact(p.GetValueOrDefault("inicio", "2026-11-10"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var motor = p.GetValueOrDefault("motor", modo == "http" ? Motores.HikvisionAnpr : Motores.Frigate);
        var sinRuido = p.ContainsKey("sin-ruido");

        var parametros = new ParametrosSimulacion
        {
            Semilla = int.Parse(p.GetValueOrDefault("semilla", "1"), CultureInfo.InvariantCulture),
            Inicio = new DateTimeOffset(dia, zona.GetUtcOffset(dia)),
            Dias = int.Parse(p.GetValueOrDefault("dias", "1"), CultureInfo.InvariantCulture),
            AutosPorDia = int.Parse(p.GetValueOrDefault("autos", "200"), CultureInfo.InvariantCulture),
            Pais = pais,
            RuidoDia = sinRuido ? RuidoLectura.Ninguno : RuidoLectura.DiaPorDefecto,
            RuidoNoche = sinRuido ? RuidoLectura.Ninguno : RuidoLectura.NochePorDefecto,
        };

        var instalacion = Instalacion(p, pais, zona, motor);
        var pasadas = GeneradorEscenario.Generar(parametros);
        var carpeta = p.GetValueOrDefault("salida", ".");
        Directory.CreateDirectory(carpeta);

        await using (var etiquetas = new StreamWriter(Path.Combine(carpeta, "etiquetas.csv"), false, new UTF8Encoding(false)))
        {
            Planillas.EscribirEtiquetas(etiquetas, Verdad.Etiquetas(pasadas, instalacion));
        }

        var pausa = TimeSpan.FromMilliseconds(int.Parse(p.GetValueOrDefault("pausa-ms", "0"), CultureInfo.InvariantCulture));
        int enviados;
        switch (modo)
        {
            case "seco":
                var eventos = SimulacionSeca.Ejecutar(pasadas, instalacion);
                await using (var lecturas = new StreamWriter(Path.Combine(carpeta, "lecturas.csv"), false, new UTF8Encoding(false)))
                {
                    Planillas.EscribirLecturas(lecturas, eventos.Select(FilaLectura.De));
                }

                enviados = eventos.Count;
                break;
            case "mqtt":
                enviados = await PublicarMqttAsync(p, pasadas, instalacion, pausa);
                break;
            case "http":
                enviados = await EnviarHttpAsync(p, pasadas, instalacion, pausa);
                break;
            default:
                return Error($"Modo desconocido: {modo}");
        }

        await salida.WriteLineAsync(
            $"{pasadas.Count} pasadas simuladas ({pasadas.Count(x => !x.Detectada)} sin detección), {enviados} {(modo == "seco" ? "eventos" : "envíos")} en modo {modo}. Planillas en {Path.GetFullPath(carpeta)}.");
        return 0;
    }

    public static int Evaluar(Dictionary<string, string> p, TextWriter salida)
    {
        if (!p.TryGetValue("etiquetas", out var rutaEtiquetas) || !p.TryGetValue("lecturas", out var rutaLecturas))
        {
            return Error("Faltan --etiquetas y --lecturas.");
        }

        var pais = Enum.Parse<Pais>(p.GetValueOrDefault("pais", "AR"), ignoreCase: true);
        IReadOnlyList<Etiqueta> etiquetas;
        IReadOnlyList<FilaLectura> lecturas;
        using (var e = new StreamReader(rutaEtiquetas, Encoding.UTF8))
        {
            etiquetas = Planillas.LeerEtiquetas(e);
        }

        using (var l = new StreamReader(rutaLecturas, Encoding.UTF8))
        {
            lecturas = Planillas.LeerLecturas(l);
        }

        var markdown = Evaluador.ComoMarkdown(Evaluador.Evaluar(etiquetas, lecturas, pais));
        if (p.TryGetValue("informe", out var rutaInforme))
        {
            File.WriteAllText(rutaInforme, markdown, new UTF8Encoding(false));
        }

        salida.Write(markdown);
        return 0;
    }

    private static InstalacionSimulada Instalacion(Dictionary<string, string> p, Pais pais, TimeZoneInfo zona, string motor)
    {
        var (ingreso, egreso) = motor == Motores.Frigate ? ("porton", "porton") : ("anpr-ingreso", "anpr-egreso");
        var camaras = motor == Motores.Frigate
            ? new Dictionary<string, ConfiguracionCamara>
            {
                ["porton"] = new() { Camara = "porton", Rol = RolCamara.PorZona, ZonaIngreso = "entrada", ZonaEgreso = "salida" },
            }
            : new Dictionary<string, ConfiguracionCamara>
            {
                ["anpr-ingreso"] = new() { Camara = "anpr-ingreso", Rol = RolCamara.Ingreso },
                ["anpr-egreso"] = new() { Camara = "anpr-egreso", Rol = RolCamara.Egreso },
            };

        return new InstalacionSimulada
        {
            Sede = new ConfiguracionSede
            {
                Sede = p.GetValueOrDefault("sede", "AR-PRUEBA-01"),
                Dispositivo = p.GetValueOrDefault("dispositivo", "minipc-01"),
                Pais = pais,
                ZonaHoraria = zona,
                Camaras = camaras,
            },
            Motor = motor,
            CamaraIngreso = ingreso,
            CamaraEgreso = egreso,
        };
    }

    private static async Task<int> PublicarMqttAsync(
        Dictionary<string, string> p, IReadOnlyList<Pasada> pasadas, InstalacionSimulada instalacion, TimeSpan pausa)
    {
        var fabrica = new MqttClientFactory();
        using var cliente = fabrica.CreateMqttClient();
        await cliente.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(p.GetValueOrDefault("mqtt-host", "127.0.0.1"), int.Parse(p.GetValueOrDefault("mqtt-puerto", "1883"), CultureInfo.InvariantCulture))
            .WithClientId("bravo-simulador")
            .Build());

        await cliente.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(LectorFrigate.TopicoDisponible).WithPayload("online").Build());
        var enviados = 0;
        foreach (var pasada in pasadas)
        {
            foreach (var m in Mensajes.Frigate(pasada, instalacion.CamaraDe(pasada), instalacion.ZonaIngreso, instalacion.ZonaEgreso))
            {
                await cliente.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(m.Topico)
                    .WithPayload(m.Carga)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
                enviados++;
            }

            if (pausa > TimeSpan.Zero)
            {
                await Task.Delay(pausa);
            }
        }

        await cliente.DisconnectAsync();
        return enviados;
    }

    private static async Task<int> EnviarHttpAsync(
        Dictionary<string, string> p, IReadOnlyList<Pasada> pasadas, InstalacionSimulada instalacion, TimeSpan pausa)
    {
        using var http = new HttpClient { BaseAddress = new Uri(p.GetValueOrDefault("conector", "http://127.0.0.1:8080")) };
        var enviados = 0;
        foreach (var pasada in pasadas)
        {
            if (Mensajes.Hikvision(pasada) is not { } xml)
            {
                continue;
            }

            using var contenido = new StringContent(xml, Encoding.UTF8);
            contenido.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            using var r = await http.PostAsync($"anpr/{instalacion.CamaraDe(pasada)}", contenido);
            if (!r.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"El conector respondió {(int)r.StatusCode} a la pasada {pasada.Clave}.");
            }

            enviados++;
            if (pausa > TimeSpan.Zero)
            {
                await Task.Delay(pausa);
            }
        }

        return enviados;
    }

    private static int Error(string mensaje)
    {
        Console.Error.WriteLine(mensaje);
        return 2;
    }

    private static Dictionary<string, string> Parametros(string[] args)
    {
        var resultado = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var clave = args[i][2..];
            var tieneValor = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            resultado[clave] = tieneValor ? args[++i] : "true";
        }

        return resultado;
    }
}
