using System.Collections;
using IBM.WMQ;
using Microsoft.Extensions.Configuration;
using NetMqReplier;

// ---------- Configuración ----------
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var settings = config.GetSection("Mq").Get<MqSettings>() ?? new MqSettings();
int workerCount = Math.Max(1, settings.WorkerThreads);

// ---------- ThreadPool ----------
// Los workers corren en hilos dedicados, pero el cliente MQ y el runtime usan el pool
// para tareas auxiliares (timers, I/O). Se lo dimensiona para que nunca tenga que
// crecer bajo carga (el crecimiento del pool es lento: ~1 hilo cada 500 ms).
ThreadPool.GetMinThreads(out int minWorker, out int minIo);
int desired = Environment.ProcessorCount + workerCount;
ThreadPool.SetMinThreads(Math.Max(minWorker, desired), Math.Max(minIo, desired));

// ---------- Cancelación (Ctrl+C) ----------
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// ---------- Conexión al Queue Manager (al arranque, una por worker) ----------
var props = new Hashtable
{
    { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
    { MQC.HOST_NAME_PROPERTY, settings.ServerIp },
    { MQC.PORT_PROPERTY, settings.ServerPort },
    { MQC.CHANNEL_PROPERTY, settings.Channel },
};
if (!string.IsNullOrEmpty(settings.User))
{
    props[MQC.USER_ID_PROPERTY] = settings.User;
    props[MQC.PASSWORD_PROPERTY] = settings.Password ?? string.Empty;
}

Console.WriteLine($"Conectando a {settings.ManagerName} en {settings.ServerIp}:{settings.ServerPort} canal {settings.Channel} ({workerCount} worker(s))...");

var workers = new ReplierWorker[workerCount];
try
{
    for (int i = 0; i < workerCount; i++)
        workers[i] = new ReplierWorker(i, settings, props);
}
catch (MQException ex)
{
    Console.Error.WriteLine($"No se pudo conectar al queue manager: CompCode={ex.CompCode} Reason={ex.ReasonCode} ({ex.Message})");
    return 1;
}

Console.WriteLine($"Escuchando en {settings.InputQueue}, respuesta por defecto a {settings.ReplyQueue}. Ctrl+C para salir.");

// ---------- Ejecución ----------
if (workerCount == 1)
{
    // Un solo worker: corre en el hilo principal, sin hilos extra
    workers[0].Run(cts.Token);
}
else
{
    // Hilos dedicados (no del pool): viven toda la ejecución bloqueados en MQGET
    var threads = new Thread[workerCount];
    for (int i = 0; i < workerCount; i++)
    {
        var worker = workers[i];
        threads[i] = new Thread(() => worker.Run(cts.Token))
        {
            Name = $"MqReplier-{i}",
            IsBackground = false,
        };
        threads[i].Start();
    }
    foreach (var t in threads)
        t.Join();
}

// ---------- Cierre ordenado ----------
long processed = 0;
foreach (var w in workers)
{
    processed += w.Processed;
    w.Dispose();
}
Console.WriteLine($"Cerrando. Mensajes procesados: {processed}");
return 0;
