namespace NetMqReplier;

public sealed class TestSettings
{
    /// <summary>Mensajes medidos en total (se reparten entre los hilos emisores).</summary>
    public int MessageCount { get; set; } = 1000;

    /// <summary>Mensajes previos por hilo que no entran en la medición.</summary>
    public int WarmupCount { get; set; } = 10;

    /// <summary>Espera máxima por cada respuesta.</summary>
    public int ReplyTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Hilos emisores en paralelo. Cada uno tiene su propia conexión al manager
    /// y manda un mensaje a la vez, así que también es la cantidad de mensajes en vuelo.
    /// </summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>
    /// Texto fijo a enviar en cada pedido. Si es null o vacío, se genera uno
    /// distinto por mensaje (secuencia, hilo y timestamp).
    /// </summary>
    public string? Message { get; set; }

    /// <summary>CCSID con el que se codifica el texto y se marca el MQMD (1208 = UTF-8).</summary>
    public int CharacterSet { get; set; } = 1208;

    /// <summary>Formato del MQMD (hasta 8 caracteres). "MQSTR" = cadena; vacío = MQFMT_NONE.</summary>
    public string Format { get; set; } = "MQSTR";
}
