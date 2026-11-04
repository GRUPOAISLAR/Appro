namespace Bravo.Video.Contratos;

/// <summary>
/// Reglas del contrato que el tipo de C# no alcanza a expresar. La ingesta las
/// aplica a cada evento del lote y rechaza solo los que fallan, con el motivo.
/// </summary>
public static class ValidadorEvento
{
    public const int LargoMaximoTexto = 64;

    public static IReadOnlyList<string> Validar(EventoVideo evento)
    {
        var errores = new List<string>();

        if (evento.EventId == Guid.Empty)
        {
            errores.Add("eventId vacío.");
        }

        if (evento.SchemaVersion != VersionContrato.Actual)
        {
            errores.Add($"schemaVersion no soportada (se espera \"{VersionContrato.Actual}\").");
        }

        Texto(errores, "sede", evento.Sede, obligatorio: true);
        Texto(errores, "dispositivo", evento.Dispositivo, obligatorio: true);
        Texto(errores, "camara", evento.Camara, obligatorio: evento is not DispositivoLatido);
        Texto(errores, "fuente.motor", evento.Fuente?.Motor, obligatorio: true);
        Texto(errores, "fuente.version", evento.Fuente?.Version, obligatorio: true);

        switch (evento)
        {
            case EventoVehiculo vehiculo when vehiculo.Patente is { } patente:
                ValidarPatente(errores, patente);
                break;
            case PersonasCruce cruce:
                Texto(errores, "linea", cruce.Linea, obligatorio: true);
                if (cruce.Entradas < 0 || cruce.Salidas < 0)
                {
                    errores.Add("entradas y salidas no pueden ser negativas.");
                }

                if (cruce.Desde is { } desde && cruce.Hasta is { } hasta && hasta < desde)
                {
                    errores.Add("hasta es anterior a desde.");
                }

                break;
            case EventoBox box:
                Texto(errores, "box", box.Box, obligatorio: true);
                break;
            case DispositivoLatido latido:
                Texto(errores, "versionConector", latido.VersionConector, obligatorio: true);
                if (latido.ColaPendiente < 0)
                {
                    errores.Add("colaPendiente no puede ser negativa.");
                }

                break;
        }

        return errores;
    }

    private static void ValidarPatente(List<string> errores, LecturaPatente patente)
    {
        if (patente.Pais is not ("AR" or "MX"))
        {
            errores.Add("patente.pais debe ser AR o MX.");
        }

        if (string.IsNullOrWhiteSpace(patente.Leida) || patente.Leida.Length > 16)
        {
            errores.Add("patente.leida vacía o de más de 16 caracteres.");
        }

        if (patente.Valida != (patente.Normalizada is not null && patente.Formato is not null))
        {
            errores.Add("patente.valida no coincide con normalizada y formato.");
        }

        if (patente.Confianza is < 0m or > 1m)
        {
            errores.Add("patente.confianza fuera de [0, 1].");
        }

        if (patente.Correcciones < 0)
        {
            errores.Add("patente.correcciones no puede ser negativa.");
        }
    }

    private static void Texto(List<string> errores, string campo, string? valor, bool obligatorio)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            if (obligatorio)
            {
                errores.Add($"{campo} vacío.");
            }

            return;
        }

        if (valor.Length > LargoMaximoTexto)
        {
            errores.Add($"{campo} supera {LargoMaximoTexto} caracteres.");
        }
    }
}
