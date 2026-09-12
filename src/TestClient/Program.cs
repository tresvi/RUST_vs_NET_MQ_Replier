using System.Collections;
using System.Diagnostics;
using IBM.WMQ;
using Microsoft.Extensions.Configuration;
using NetMqReplier;

// Parámetros desde appsettings.json (sección "Test"). Argumentos opcionales los sobreescriben:
//   TestClient [cantidad] [warmup] [concurrencia]
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();
var settings = config.GetSection("Mq").Get<MqSettings>() ?? new MqSettings();
var test = config.GetSection("Test").Get<TestSettings>() ?? new TestSettings();

int count = args.Length > 0 ? int.Parse(args[0]) : test.MessageCount;
int warmup = args.Length > 1 ? int.Parse(args[1]) : test.WarmupCount;
int concurrency = Math.Max(1, args.Length > 2 ? int.Parse(args[2]) : test.Concurrency);

ThreadPool.GetMinThreads(out int minWorker, out int minIo);
int desired = Environment.ProcessorCount + concurrency;
ThreadPool.SetMinThreads(Math.Max(minWorker, desired), Math.Max(minIo, desired));

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

Console.WriteLine($"Conectando a {settings.ManagerName} en {settings.ServerIp}:{settings.ServerPort} canal {settings.Channel} ({concurrency} hilo(s) emisor(es))...");

// Reparto de la cuota total entre hilos; los primeros absorben el resto de la división
var senders = new SenderWorker[concurrency];
for (int i = 0; i < concurrency; i++)
{
    int quota = count / concurrency + (i < count % concurrency ? 1 : 0);
    senders[i] = new SenderWorker(i, settings, test, props, quota);
}

Console.WriteLine($"Warm-up: {warmup} mensajes por hilo. Medición: {count} mensajes en total, {concurrency} en vuelo.");

// La barrera arranca el reloj de pared cuando el último hilo terminó su warm-up
var wall = new Stopwatch();
using var barrier = new Barrier(concurrency, _ => wall.Start());
bool verbose = concurrency == 1;

var threads = new Thread[concurrency];
for (int i = 0; i < concurrency; i++)
{
    var sender = senders[i];
    threads[i] = new Thread(() => sender.Run(warmup, barrier, verbose)) { Name = $"Sender-{i}" };
    threads[i].Start();
}
foreach (var t in threads)
    t.Join();
wall.Stop();

// ---------- Métricas agregadas ----------
var latencies = new List<double>(count);
int errors = 0;
foreach (var s in senders)
{
    latencies.AddRange(s.Latencies.AsSpan(0, s.Measured));
    errors += s.Errors;
    s.Dispose();
}

if (latencies.Count == 0)
{
    Console.WriteLine("Sin mediciones.");
    return 1;
}

latencies.Sort();
double P(double p) => latencies[Math.Min(latencies.Count - 1, (int)Math.Ceiling(p * latencies.Count) - 1)];
double seconds = wall.Elapsed.TotalSeconds;

Console.WriteLine();
Console.WriteLine($"Mensajes medidos: {latencies.Count}  errores: {errors}  concurrencia: {concurrency}");
Console.WriteLine($"Tiempo de pared: {seconds:F3} s  throughput: {latencies.Count / seconds:F0} msg/s");
Console.WriteLine($"Latencia: avg={latencies.Average():F3} ms  min={latencies[0]:F3}  p50={P(0.50):F3}  p90={P(0.90):F3}  p99={P(0.99):F3}  max={latencies[^1]:F3}");
return errors == 0 ? 0 : 1;
