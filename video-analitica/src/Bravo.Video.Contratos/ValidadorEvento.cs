namespace Bravo.Video.Contratos;

/// <summary>
/// Reglas del contrato que el tipo de C# no alcanza a expresar. La ingesta las
/// aplica a cada evento del lote y rechaza solo los que fallan, con el motivo.
/// </summary>
public static class ValidadorEvento
{
    public const int LargoMaximoTexto = 64;
    public const int LargoMaximoIdOrigen = 128;
    public const int MaximoMotores = 20;

    // Los largos coinciden con las columnas de la ingesta: un evento que pasa la
    // validación siempre entra en la base, y uno que no entraría se rechaza con
    // motivo en vez de trabar el lote entero con un error de la base.

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
            case EventoVehiculo vehiculo:
                IdOrigen(errores, vehiculo.IdOrigen);
                if (vehiculo.Patente is { } patente)
                {
                    ValidarPatente(errores, patente);
                }

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
                IdOrigen(errores, box.IdOrigen);
                break;
            case DispositivoLatido latido:
                Texto(errores, "versionConector", latido.VersionConector, obligatorio: true);
                if (latido.ColaPendiente < 0)
                {
                    errores.Add("colaPendiente no puede ser negativa.");
                }

                if (latido.Motores is null || latido.Motores.Count > MaximoMotores)
                {
                    errores.Add($"motores tiene que tener entre 0 y {MaximoMotores} elementos.");
                }
                else
                {
                    foreach (var motor in latido.Motores)
                    {
                        Texto(errores, "motores.nombre", motor.Nombre, obligatorio: true);
                        if (motor.Estado is not (EstadosMotor.EnLinea or EstadosMotor.FueraDeLinea or EstadosMotor.Desconocido))
                        {
                            errores.Add("motores.estado inválido.");
                        }
                    }
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

        if (patente.Normalizada is { Length: > 16 } || patente.Formato is { Length: > 32 })
        {
            errores.Add("patente.normalizada (16) o patente.formato (32) demasiado largos.");
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

    private static void IdOrigen(List<string> errores, string? idOrigen)
    {
        if (idOrigen is { Length: > LargoMaximoIdOrigen })
        {
            errores.Add($"idOrigen supera {LargoMaximoIdOrigen} caracteres.");
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
