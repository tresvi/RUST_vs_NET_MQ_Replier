namespace NetMqReplier;

public sealed class MqSettings
{
    public string Channel { get; set; } = "CHANNEL1";
    public string ManagerName { get; set; } = "MQGD";
    public string ServerIp { get; set; } = "localhost";
    public int ServerPort { get; set; } = 1414;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string InputQueue { get; set; } = "BNA.TU5.PEDIDO";
    public string ReplyQueue { get; set; } = "BNA.TU5.RESPUESTA";

    /// <summary>
    /// Tiempo máximo que bloquea cada MQGET antes de volver a chequear cancelación.
    /// Solo afecta al apagado (Ctrl+C), no a la latencia de respuesta.
    /// </summary>
    public int GetWaitIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Cantidad de hilos consumidores. Cada uno tiene su propia conexión al manager
    /// y sus propios handles de cola, así no comparten nada ni necesitan locks.
    /// </summary>
    public int WorkerThreads { get; set; } = 1;

    /// <summary>
    /// Texto fijo a enviar como respuesta (UTF-8, formato MQSTR).
    /// Si es null o vacío, se responde con el echo del mensaje recibido.
    /// </summary>
    public string? ReplyMessage { get; set; }
}
