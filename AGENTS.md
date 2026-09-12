# AGENTS.md

Guía para agentes de código (y humanos) que trabajen en este repositorio.
El README tiene el contexto funcional y las decisiones de diseño; este archivo
resume lo operativo.

## Qué es

Benchmark de un *replier* IBM MQ (request/reply, echo) en .NET 10 JIT, con
variantes .NET AOT y Rust planificadas. **La prioridad del replier es la
latencia de respuesta**: cualquier cambio en el hot loop debe justificarse en
esos términos.

## Estructura

```
RUST_vs_NET_MQ_Replier.slnx
src/
  NetMqReplier/        Replier .NET 10 (JIT). Hilo principal + N workers opcionales.
    Program.cs         Config, ThreadPool, conexión al arranque, lanzamiento de workers.
    ReplierWorker.cs   Hot loop: MQGET -> respuesta -> MQPUT. Conexión propia por worker.
    MqSettings.cs      POCO de la sección "Mq" (compartido con TestClient por link).
    appsettings.json   Valores por defecto; admite comentarios //.
  TestClient/          Generador de carga y medición de latencia/throughput.
    Program.cs         Reparte la cuota entre hilos, Barrier de arranque, métricas agregadas.
    SenderWorker.cs    Un hilo emisor: put -> get por CorrelId, conexión propia.
    TestSettings.cs    POCO de la sección "Test".
    appsettings.json   Misma sección "Mq" + sección "Test".
docs/                  Vacío por ahora.
```

## Comandos

```bash
dotnet build -c Release
dotnet run -c Release --project src/NetMqReplier            # replier, Ctrl+C para salir
dotnet run -c Release --project src/TestClient -- 2000 10 8 # [cantidad] [warmup] [concurrencia]
```

Para probar con otro `WorkerThreads` sin tocar el fuente, editar el
`appsettings.json` copiado a `src/NetMqReplier/bin/Release/net10.0/`.

No hay tests unitarios: la verificación es correr replier + TestClient contra
el manager y comprobar `errores: 0`. El TestClient sale con código 1 si hubo
timeouts o echos incorrectos.

## Entorno

- .NET SDK 10 (hay varios SDK instalados; el `.csproj` fija `net10.0`).
- Queue manager `MQGD` en `192.168.0.31:1414`, canal `CHANNEL1`, sin
  credenciales. Colas `BNA.TU5.PEDIDO` (entrada) y `BNA.TU5.RESPUESTA`
  (respuesta por defecto). Si no hay manager, el replier sale con
  `Reason=2538` (`MQRC_HOST_NOT_AVAILABLE`).
- Cliente MQ: NuGet `IBMMQDotnetClient` (managed, no requiere instalar MQ).
  El aviso `"dspmqver" no se reconoce...` al arrancar es de la librería y es
  inofensivo.

## Reglas y trampas conocidas

- **No activar `InvariantGlobalization`**: `IBM.WMQ.MQMessage` instancia la
  cultura `en-US` en su constructor estático y falla en runtime.
- **No compartir `MQQueueManager` entre hilos.** Cada worker/sender abre su
  propia conexión. Si se agrega concurrencia, seguir ese patrón; no meter locks
  en el hot loop.
- **Hot loop sin logging, sin `string`, sin allocations evitables.** El echo se
  hace en `byte[]`; la respuesta fija (`ReplyMessage`) se codifica una vez en
  el constructor del worker.
- **Workers en `Thread` dedicados, no en el ThreadPool** (bloquean en `MQGET`).
  El pool se dimensiona con `SetMinThreads(ProcessorCount + WorkerThreads)`
  para el uso auxiliar del cliente MQ; mantener esa relación si cambia el
  número de hilos.
- **`ReplyToQ` del MQMD tiene prioridad** sobre `Mq.ReplyQueue`; el nombre
  viene con padding a 48 caracteres, se hace `TrimEnd()`.
- **`CorrelId = MsgId` del pedido** y `MsgType = MQMT_REPLY`. El emisor de
  referencia manda `CorrelId = NONE` y hace `MQGET` con `MATCH_CORREL_ID`; no
  cambiar sin coordinar con el otro lado.
- `WaitInterval` del GET es finito (5 s) solo para que Ctrl+C funcione; no
  afecta la latencia con mensajes disponibles.
- `appsettings.json` se copia a la salida (`PreserveNewest`); los comentarios
  `//` son válidos para el lector de configuración de .NET.

## Cuando se agregue una variante (AOT, Rust)

- Mantener el mismo contrato: mismas colas, `CorrelId = MsgId`, `MQMT_REPLY`,
  no persistente, echo byte a byte o `ReplyMessage` fijo, y la misma forma de
  configuración (`Mq.*`) para poder usar el mismo TestClient.
- Medir con la misma matriz del README (workers × concurrencia) y agregar
  arranque y memoria residente, que es donde se esperan las diferencias.
- AOT: `IBMMQDotnetClient` no declara compatibilidad con trimming/AOT; esperar
  warnings y probar en runtime antes de asumir que funciona.
- Rust/Go: requieren la biblioteca C de IBM (cliente redistribuible); ver la
  sección correspondiente del README.

## Convenciones

- Código y comentarios en español, como el resto del repo.
- Commits en formato *conventional commits* (`feat:`, `fix:`, `docs:`, ...).
- No commitear `bin/`, `obj/` (ya están en `.gitignore`).
