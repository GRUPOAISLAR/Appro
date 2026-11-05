using System.Text.Json;

namespace Bravo.Video.Conector.Tests;

public class EjemploPruebas
{
    [Fact]
    public void El_archivo_de_ejemplo_es_valido()
    {
        // Si alguien cambia la configuración y no el ejemplo, esto falla.
        var ruta = Path.Combine(AppContext.BaseDirectory, "config", "conector.ejemplo.json");
        var archivo = JsonSerializer.Deserialize<ArchivoConfiguracion>(File.ReadAllText(ruta), ConfiguracionConector.OpcionesArchivo)!;

        var config = ConfiguracionConector.Armar(archivo, Ayudas.Entorno());

        Assert.Equal(3, config.Sede.Camaras.Count);
        Assert.Equal(2, config.CamarasHttp.Count);
        Assert.Single(config.Sensores);
        Assert.NotNull(config.Mqtt);
    }
}
