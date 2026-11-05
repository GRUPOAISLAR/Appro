using Bravo.Video.Patentes;

namespace Bravo.Video.Tests;

// Todas las patentes de estas pruebas son inventadas.
public class PatentesPruebas
{
    [Theory]
    [InlineData("AA000AA", "AR_MERCOSUR_AUTO")]
    [InlineData("aa 000 aa", "AR_MERCOSUR_AUTO")]
    [InlineData("AAA000", "AR_VIEJA_AUTO")]
    [InlineData("AAA-000", "AR_VIEJA_AUTO")]
    [InlineData("A000AAA", "AR_MERCOSUR_MOTO")]
    [InlineData("000AAA", "AR_VIEJA_MOTO")]
    public void Argentina_formatos_validos(string leida, string formato)
    {
        var r = NormalizadorPatentes.Normalizar(leida, Pais.AR);
        Assert.True(r.Valida);
        Assert.Equal(formato, r.Formato!.Nombre);
        Assert.Empty(r.Correcciones);
    }

    [Theory]
    [InlineData("XXX000", "MX_AUTO_6")]
    [InlineData("XXX0000", "MX_AUTO_7")]
    [InlineData("XXX000X", "MX_AUTO_7L")]
    [InlineData("XX0000X", "MX_CAMION")]
    [InlineData("Y000XX", "MX_MOTO")]
    public void Mexico_formatos_validos(string leida, string formato)
    {
        var r = NormalizadorPatentes.Normalizar(leida, Pais.MX);
        Assert.True(r.Valida);
        Assert.Equal(formato, r.Formato!.Nombre);
    }

    [Fact]
    public void Corrige_parecidos_segun_posicion()
    {
        // Mercosur LLNNNLL: "0" en posición de letra → "O"; "O" en posición de número → "0".
        var r = NormalizadorPatentes.Normalizar("0A1O2AA", Pais.AR);

        Assert.True(r.Valida);
        Assert.Equal("OA102AA", r.Normalizada);
        Assert.Equal([new Correccion(0, '0', 'O'), new Correccion(3, 'O', '0')], r.Correcciones);
    }

    [Theory]
    [InlineData('O', '0')]
    [InlineData('I', '1')]
    [InlineData('B', '8')]
    [InlineData('S', '5')]
    [InlineData('Z', '2')]
    [InlineData('G', '6')]
    public void Cada_par_de_parecidos_corrige_en_los_dos_sentidos(char letra, char digito)
    {
        var aNumero = NormalizadorPatentes.Normalizar($"AA{letra}00AA", Pais.AR);
        Assert.Equal($"AA{digito}00AA", aNumero.Normalizada);

        // Formato viejo LLLNNN: el dígito en posición de letra pasa a letra.
        var aLetra = NormalizadorPatentes.Normalizar($"A{digito}A000", Pais.AR);
        Assert.Equal($"A{letra}A000", aLetra.Normalizada);
    }

    [Fact]
    public void No_corrige_pares_que_no_estan_en_la_lista()
    {
        // "3" en la primera posición (letra en los dos formatos de 7) no se
        // parece a ninguna letra de la lista: no hay formato posible.
        var r = NormalizadorPatentes.Normalizar("3A000AA", Pais.AR);
        Assert.False(r.Valida);
        Assert.False(r.Ambigua);
    }

    [Fact]
    public void Prefiere_el_formato_con_menos_correcciones()
    {
        // 7 caracteres en AR: Mercosur auto (LLNNNLL) o moto (LNNNLLL).
        // "AB000AA" encaja limpio como auto; como moto necesitaría B→8 y 0→O.
        var r = NormalizadorPatentes.Normalizar("AB000AA", Pais.AR);
        Assert.Equal("AR_MERCOSUR_AUTO", r.Formato!.Nombre);
        Assert.Equal("AB000AA", r.Normalizada);
    }

    [Fact]
    public void Empate_entre_formatos_queda_ambiguo_e_invalido()
    {
        // "A8000AA": auto necesita 8→B (1 corrección); moto necesita 0→O en la
        // posición 4 (1 corrección). Empate: no se elige.
        var r = NormalizadorPatentes.Normalizar("A8000AA", Pais.AR);
        Assert.False(r.Valida);
        Assert.True(r.Ambigua);
        Assert.Null(r.Formato);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AA00AA")]
    [InlineData("AA0000AA")]
    [InlineData("AÑ000AA")]
    [InlineData("AA_000AA")]
    public void Lecturas_invalidas(string leida)
    {
        var r = NormalizadorPatentes.Normalizar(leida, Pais.AR);
        Assert.False(r.Valida);
        Assert.Equal(leida, r.Leida);
    }

    [Fact]
    public void El_pais_decide_los_formatos()
    {
        // "Y000XX" es moto en México y no existe en Argentina.
        Assert.True(NormalizadorPatentes.Normalizar("Y000XX", Pais.MX).Valida);
        Assert.False(NormalizadorPatentes.Normalizar("Y000XX", Pais.AR).Valida);
    }

    [Fact]
    public void Hash_es_estable_depende_del_pais_y_de_la_clave()
    {
        var clave = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var otraClave = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var hash = new HashPatente(clave);

        var a = hash.Calcular("AA000AA", Pais.AR);
        Assert.Equal(a, hash.Calcular("AA000AA", Pais.AR));
        Assert.NotEqual(a, hash.Calcular("AA000AA", Pais.MX));
        Assert.NotEqual(a, new HashPatente(otraClave).Calcular("AA000AA", Pais.AR));
        Assert.True(HashPatente.EsHash(a));
        Assert.DoesNotContain("AA000AA", a);
        Assert.Equal(HashPatente.Prefijo.Length + 64, a.Length);
    }

    [Fact]
    public void Hash_rechaza_clave_corta()
    {
        Assert.Throws<ArgumentException>(() => new HashPatente(new byte[16]));
    }
}
