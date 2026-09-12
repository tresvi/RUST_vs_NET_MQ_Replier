using System.Collections;
using System.Text;
using IBM.WMQ;

namespace NetMqReplier;

/// <summary>
/// Un consumidor: conexión propia al manager, cola de entrada abierta y cache de colas de salida.
/// No comparte nada con otros workers, así el hot loop corre sin sincronización.
/// </summary>
public sealed class ReplierWorker : IDisposable
{
    private const int OpenInOptions = MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING;
    private const int OpenOutOptions = MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING;

    private readonly int _id;
    private readonly MqSettings _settings;
    private readonly MQQueueManager _queueManager;
    private readonly MQQueue _queueIn;
    private readonly Dictionary<string, MQQueue> _outQueues = new(StringComparer.Ordinal);
    private readonly MQGetMessageOptions _gmo;
    private readonly MQPutMessageOptions _pmo;

    // Respuesta fija precalculada (null => echo del pedido)
    private readonly byte[]? _fixedReply;

    public long Processed { get; private set; }

    /// <summary>Conecta y abre las colas. Se llama en el hilo principal, al arranque.</summary>
    public ReplierWorker(int id, MqSettings settings, Hashtable connectionProps)
    {
        _id = id;
        _settings = settings;
        _queueManager = new MQQueueManager(settings.ManagerName, connectionProps);
        _queueIn = _queueManager.AccessQueue(settings.InputQueue, OpenInOptions);
        _outQueues[settings.ReplyQueue] = _queueManager.AccessQueue(settings.ReplyQueue, OpenOutOptions);

        _gmo = new MQGetMessageOptions
        {
            Options = MQC.MQGMO_WAIT | MQC.MQGMO_NO_SYNCPOINT | MQC.MQGMO_FAIL_IF_QUIESCING,
            WaitInterval = settings.GetWaitIntervalMs,
        };
        _pmo = new MQPutMessageOptions
        {
            Options = MQC.MQPMO_NO_SYNCPOINT | MQC.MQPMO_NEW_MSG_ID,
        };

        if (!string.IsNullOrEmpty(settings.ReplyMessage))
            _fixedReply = Encoding.UTF8.GetBytes(settings.ReplyMessage);
    }

    public void Run(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = new MQMessage();
            try
            {
                _queueIn.Get(request, _gmo);
            }
            catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                continue; // timeout del WaitInterval: volver a chequear cancelación
            }

            var reply = new MQMessage
            {
                MessageType = MQC.MQMT_REPLY,
                Persistence = MQC.MQPER_NOT_PERSISTENT,
                MessageId = MQC.MQMI_NONE,
                CorrelationId = request.MessageId,
            };

            if (_fixedReply is null)
            {
                // Echo del payload tal cual (sin conversiones de encoding)
                reply.Format = request.Format;
                reply.CharacterSet = request.CharacterSet;
                reply.Encoding = request.Encoding;
                reply.Write(request.ReadBytes(request.MessageLength));
            }
            else
            {
                reply.Format = MQC.MQFMT_STRING;
                reply.CharacterSet = 1208; // UTF-8
                reply.Write(_fixedReply);
            }

            string replyQueueName = string.IsNullOrWhiteSpace(request.ReplyToQueueName)
                ? _settings.ReplyQueue
                : request.ReplyToQueueName.TrimEnd();

            try
            {
                if (!_outQueues.TryGetValue(replyQueueName, out var queueOut))
                {
                    queueOut = _queueManager.AccessQueue(replyQueueName, OpenOutOptions);
                    _outQueues[replyQueueName] = queueOut;
                }

                queueOut.Put(reply, _pmo);
                Processed++;
            }
            catch (MQException ex)
            {
                // Un ReplyToQ inválido en un mensaje no debe tumbar el worker
                Console.Error.WriteLine($"[worker {_id}] Error respondiendo a '{replyQueueName}': CompCode={ex.CompCode} Reason={ex.ReasonCode}");
            }
        }
    }

    public void Dispose()
    {
        foreach (var q in _outQueues.Values)
            q.Close();
        _queueIn.Close();
        _queueManager.Disconnect();
    }
}
