using System.Text;
using Bravo.Video.Conector.Cola;
using Bravo.Video.Lectores;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Bravo.Video.Conector.Adaptadores;

/// <summary>
/// Recibe lo que mandan por HTTP la cámara ANPR y el sensor de conteo.
///
/// Las cámaras no saben mandar un token, así que se acepta solo desde la IP
/// configurada para cada una (la red del local es cerrada). Las imágenes que
/// manda la cámara junto con el XML se leen y se descartan sin guardarse.
/// </summary>
public sealed class ReceptorHttp(
    ColaDisco cola,
    ConfiguracionConector configuracion,
    EstadoConector estado,
    TimeProvider reloj,
    ILogger<ReceptorHttp> log)
{
    public async Task<IResult> AnprAsync(string camara, HttpRequest pedido, CancellationToken ct)
    {
        if (!configuracion.CamarasHttp.TryGetValue(camara, out var origen))
        {
            return Results.NotFound();
        }

        if (!origen.Acepta(pedido.HttpContext.Connection.RemoteIpAddress))
        {
            log.LogWarning("Pedido a /anpr/{Camara} desde una IP no autorizada.", camara);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var xml = await ExtraerXmlAsync(pedido, ct);
        if (xml is null)
        {
            estado.Invalido();
            return Results.BadRequest();
        }

        LecturaVehiculo? lectura;
        try
        {
            lectura = LectorHikvisionAnpr.Leer(xml, camara, origen.Firmware, configuracion.Sede.ZonaHoraria);
        }
        catch (MensajeInvalidoException e)
        {
            estado.Invalido();
            log.LogWarning("Alerta de {Camara} descartada: {Motivo}", camara, e.Message);
            return Results.BadRequest();
        }

        estado.MensajeRecibido($"{Motores.HikvisionAnpr}:{camara}");

        // Alertas que no son de patente (latidos de la cámara): se contestan 200 y no se encolan.
        if (lectura is null)
        {
            return Results.Ok();
        }

        var armado = ArmadorEventos.Vehiculo(lectura, configuracion.Sede);
        if (armado.Evento is null)
        {
            estado.Descarte();
            log.LogInformation("Lectura de {Camara} descartada: {Motivo}", camara, armado.MotivoDescarte);
            return Results.Ok();
        }

        return cola.Encolar(armado.Evento) ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    public async Task<IResult> ConteoAsync(string sensor, HttpRequest pedido, CancellationToken ct)
    {
        if (!configuracion.Sensores.TryGetValue(sensor, out var origen))
        {
            return Results.NotFound();
        }

        if (!origen.Acepta(pedido.HttpContext.Connection.RemoteIpAddress))
        {
            log.LogWarning("Pedido a /conteo/{Sensor} desde una IP no autorizada.", sensor);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        using var lector = new StreamReader(pedido.Body, Encoding.UTF8);
        var json = await lector.ReadToEndAsync(ct);

        IReadOnlyList<ConteoLinea> conteos;
        try
        {
            conteos = LectorMilesight.Leer(json);
        }
        catch (MensajeInvalidoException e)
        {
            estado.Invalido();
            log.LogWarning("Reporte de {Sensor} descartado: {Motivo}", sensor, e.Message);
            return Results.BadRequest();
        }

        estado.MensajeRecibido($"{Motores.Milesight}:{sensor}");
        // Límite conocido: el id de cada línea sale de la hora de recepción, porque el
        // formato de hora del reporte no está confirmado con un equipo real. Si el
        // sensor reenvía el mismo reporte, se cuenta dos veces. Se corrige cuando haya
        // una captura real (preparativos.md). Mientras tanto, todo o nada: un reporte
        // nunca queda encolado a medias.
        var recibido = reloj.GetUtcNow();
        var eventos = conteos
            .Select(c => (Contratos.EventoVideo)ArmadorEventos.Personas(c, sensor, origen.Firmware, recibido, configuracion.Sede))
            .ToList();

        return cola.EncolarTodos(eventos) ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// XML de la alerta: el cuerpo entero si viene como XML, o la primera parte XML
    /// de un multipart. Las demás partes (imágenes) se leen para vaciar el pedido y
    /// se descartan.
    /// </summary>
    internal static async Task<string?> ExtraerXmlAsync(HttpRequest pedido, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(pedido.ContentType, out var tipo) || !tipo.MediaType.HasValue)
        {
            return await LeerSiEsXmlAsync(pedido.Body, ct);
        }

        if (!tipo.MediaType.Value!.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            return await LeerSiEsXmlAsync(pedido.Body, ct);
        }

        var limite = HeaderUtilities.RemoveQuotes(tipo.Boundary).Value;
        if (string.IsNullOrEmpty(limite))
        {
            return null;
        }

        var lector = new MultipartReader(limite, pedido.Body);
        string? xml = null;
        while (await lector.ReadNextSectionAsync(ct) is { } seccion)
        {
            if (xml is null && EsXml(seccion.ContentType, seccion.ContentDisposition))
            {
                xml = await LeerSiEsXmlAsync(seccion.Body, ct);
            }
            else
            {
                await seccion.Body.CopyToAsync(Stream.Null, ct);
            }
        }

        return xml;
    }

    private static bool EsXml(string? contentType, string? disposicion) =>
        contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true
        || disposicion?.Contains(".xml", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<string?> LeerSiEsXmlAsync(Stream cuerpo, CancellationToken ct)
    {
        using var lector = new StreamReader(cuerpo, Encoding.UTF8);
        var texto = await lector.ReadToEndAsync(ct);
        return texto.TrimStart().StartsWith('<') ? texto : null;
    }
}
