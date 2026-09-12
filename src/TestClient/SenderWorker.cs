using System.Collections;
using System.Diagnostics;
using System.Text;
using IBM.WMQ;

namespace NetMqReplier;

/// <summary>
/// Un hilo emisor: conexión propia, manda un pedido y espera su respuesta (por CorrelId),
/// repitiendo hasta completar su cuota. Guarda las latencias medidas localmente.
/// </summary>
public sealed class SenderWorker : IDisposable
{
    private readonly int _id;
    private readonly MqSettings _settings;
    private readonly MQQueueManager _queueManager;
    private readonly MQQueue _queueOut;
    private readonly MQQueue _queueIn;
    private readonly MQPutMessageOptions _pmo;
    private readonly MQGetMessageOptions _gmo;

    private readonly Encoding _encoding;
    private readonly int _characterSet;
    private readonly string _format;
    private readonly byte[]? _fixedPayload; // null => texto generado por mensaje

    public double[] Latencies { get; }
    public int Measured { get; private set; }
    public int Errors { get; private set; }

    public SenderWorker(int id, MqSettings settings, TestSettings test, Hashtable connectionProps, int messageCount)
    {
        _id = id;
        _settings = settings;
        _queueManager = new MQQueueManager(settings.ManagerName, connectionProps);
        _queueOut = _queueManager.AccessQueue(settings.InputQueue, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);
        _queueIn = _queueManager.AccessQueue(settings.ReplyQueue, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING);

        _pmo = new MQPutMessageOptions { Options = MQC.MQPMO_NO_SYNCPOINT | MQC.MQPMO_NEW_MSG_ID };
        _gmo = new MQGetMessageOptions
        {
            Options = MQC.MQGMO_WAIT | MQC.MQGMO_NO_SYNCPOINT,
            WaitInterval = test.ReplyTimeoutMs,
            MatchOptions = MQC.MQMO_MATCH_CORREL_ID,
        };

        _characterSet = test.CharacterSet;
        _encoding = Ccsid.GetEncoding(test.CharacterSet);
        _format = string.IsNullOrWhiteSpace(test.Format) ? MQC.MQFMT_NONE : test.Format;
        if (!string.IsNullOrEmpty(test.Message))
            _fixedPayload = _encoding.GetBytes(test.Message);

        Latencies = new double[messageCount];
    }

    /// <summary>
    /// Ejecuta warm-up, espera en la barrera a que todos los hilos terminen el suyo
    /// (así el reloj de pared arranca con todos listos) y luego la fase medida.
    /// </summary>
    public void Run(int warmupCount, Barrier startBarrier, bool verbose)
    {
        for (int i = 0; i < warmupCount; i++)
            RoundTrip(i, measured: false, verbose);

        startBarrier.SignalAndWait();

        for (int i = 0; i < Latencies.Length; i++)
            RoundTrip(warmupCount + i, measured: true, verbose && i < 5);
    }

    private void RoundTrip(int seq, bool measured, bool verbose)
    {
        byte[] payload = _fixedPayload
            ?? _encoding.GetBytes($"hola mundo #{seq} hilo {_id} ñandú {DateTime.UtcNow:O}");

        var put = new MQMessage
        {
            Format = _format,
            CharacterSet = _characterSet,
            MessageId = MQC.MQMI_NONE,
            CorrelationId = MQC.MQCI_NONE,
            Persistence = MQC.MQPER_NOT_PERSISTENT,
            ReplyToQueueName = _settings.ReplyQueue,
        };
        put.Write(payload);

        long start = Stopwatch.GetTimestamp();
        _queueOut.Put(put, _pmo);

        var get = new MQMessage { CorrelationId = put.MessageId };
        try
        {
            _queueIn.Get(get, _gmo);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
        {
            Errors++;
            Console.WriteLine($"[hilo {_id}] #{seq} TIMEOUT sin respuesta");
            return;
        }
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // Se compara en bytes: el replier hace echo sin conversiones y debe devolver
        // el mismo payload, formato y CCSID. (Con ReplyMessage fijo en el replier, ok=false es esperado.)
        byte[] echo = get.ReadBytes(get.MessageLength);
        bool ok = echo.AsSpan().SequenceEqual(payload)
                  && get.MessageType == MQC.MQMT_REPLY
                  && get.CharacterSet == _characterSet
                  && get.Format.TrimEnd() == _format.TrimEnd();
        if (!ok) Errors++;
        if (measured) Latencies[Measured++] = ms;

        if (!ok || verbose)
        {
            string text = _encoding.GetString(echo);
            Console.WriteLine($"[hilo {_id}] #{seq}{(measured ? "" : " (warmup)")} {ms:F2} ms  ok={ok}  fmt={get.Format.TrimEnd()} ccsid={get.CharacterSet} echo=\"{text}\"");
        }
    }

    public void Dispose()
    {
        _queueOut.Close();
        _queueIn.Close();
        _queueManager.Disconnect();
    }
}
