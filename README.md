# RUST_vs_NET_MQ_Replier

Comparación de un *replier* de IBM MQ (request/reply) implementado en distintas
tecnologías: .NET (JIT), .NET AOT y Rust. El objetivo es medir latencia de
respuesta, throughput, arranque y consumo bajo el mismo escenario.

Estado actual:

| Componente | Estado |
|---|---|
| `src/NetMqReplier` — replier en .NET 10 (JIT) | Funcional, probado contra un manager real |
| `src/TestClient` — generador de carga y medición | Funcional |
| Replier .NET AOT | Pendiente |
| Replier Rust | Pendiente (ver [Rust y otros lenguajes](#rust-y-otros-lenguajes)) |

## Escenario

```
TestClient ──PUT──▶ BNA.TU5.PEDIDO ──GET──▶ NetMqReplier
    ▲                                            │
    └──GET (por CorrelId)── BNA.TU5.RESPUESTA ◀──PUT──┘
```

- Queue manager `MQGD`, canal `CHANNEL1`, host `192.168.0.31:1414`, transporte
  cliente TCP (managed).
- El pedido llega con `CorrelId = NONE`; la respuesta se emite con
  `CorrelId = MsgId` del pedido (patrón request/reply estándar de MQ). El
  cliente hace `MQGET` con `MQMO_MATCH_CORREL_ID`.
- Mensajes no persistentes, sin syncpoint, `MQSTR` en UTF-8 (CCSID 1208).

Los parámetros de conexión, colas y comportamiento se leen de `appsettings.json`
de cada proyecto (ver [Configuración](#configuración)).

## Cómo correr

Requiere .NET SDK 10. No hace falta instalar el cliente de IBM MQ: el paquete
NuGet `IBMMQDotnetClient` trae el cliente managed completo.

```bash
dotnet build -c Release
```

Replier (queda escuchando hasta Ctrl+C):

```bash
dotnet run -c Release --project src/NetMqReplier
```

Cliente de prueba (`[cantidad] [warmup] [concurrencia]`, opcionales; sin
argumentos toma la sección `Test` de su `appsettings.json`):

```bash
dotnet run -c Release --project src/TestClient -- 2000 10 8
```

Salida del cliente:

```
Mensajes medidos: 2000  errores: 0  concurrencia: 8
Tiempo de pared: 1.013 s  throughput: 1973 msg/s
Latencia: avg=3.892 ms  min=0.907  p50=3.365  p90=5.968  p99=13.204  max=26.506
```

> El aviso `"dspmqver" no se reconoce como un comando...` que imprime la
> librería de IBM al arrancar es inofensivo: intenta detectar una instalación
> local de MQ que no existe ni hace falta.

## Configuración

### `src/NetMqReplier/appsettings.json`

| Clave | Default | Descripción |
|---|---|---|
| `Mq.Channel` | `CHANNEL1` | Canal SVRCONN |
| `Mq.ManagerName` | `MQGD` | Nombre del queue manager |
| `Mq.ServerIp` / `Mq.ServerPort` | `192.168.0.31` / `1414` | Host y puerto del listener |
| `Mq.User` / `Mq.Password` | `null` | Credenciales; si `User` es null no se envían |
| `Mq.InputQueue` | `BNA.TU5.PEDIDO` | Cola donde escucha |
| `Mq.ReplyQueue` | `BNA.TU5.RESPUESTA` | Cola de respuesta **por defecto**: solo se usa si el pedido no trae `ReplyToQ` en el MQMD |
| `Mq.GetWaitIntervalMs` | `5000` | Tiempo máximo de bloqueo de cada `MQGET`. Solo afecta la latencia de apagado (Ctrl+C), no la de respuesta |
| `Mq.WorkerThreads` | `1` | Hilos consumidores, cada uno con su propia conexión |
| `Mq.ReplyMessage` | `null` | Texto fijo de respuesta (`MQSTR`/UTF-8). Si es null o vacío, se responde con el echo exacto del mensaje recibido |

### `src/TestClient/appsettings.json`

Misma sección `Mq` (para apuntar al mismo manager y colas) más:

| Clave | Default | Descripción |
|---|---|---|
| `Test.MessageCount` | `1000` | Mensajes medidos en total (se reparten entre los hilos) |
| `Test.WarmupCount` | `10` | Mensajes previos por hilo, excluidos de la medición |
| `Test.ReplyTimeoutMs` | `5000` | Espera máxima por cada respuesta; un timeout cuenta como error |
| `Test.Concurrency` | `1` | Hilos emisores en paralelo = mensajes en vuelo |

Los archivos admiten comentarios `//` (el lector de configuración de .NET los
tolera).

## Decisiones de diseño

### Replier (`src/NetMqReplier`)

**Prioridad: latencia de respuesta.** Todo lo que se puede hacer una sola vez se
hace en el arranque; el hot loop es `MQGET` → armar respuesta → `MQPUT` sin
sincronización, sin logging y sin conversiones.

- **Conexión y colas abiertas al arrancar.** La conexión al manager, el handle
  de la cola de entrada y el de la cola de respuesta por defecto se abren
  antes de empezar a consumir. Los objetos `MQGetMessageOptions` /
  `MQPutMessageOptions` se crean una sola vez y se reutilizan.
- **Cache de colas de salida.** Si un pedido trae `ReplyToQ`, se usa esa cola;
  el handle se abre la primera vez y queda cacheado por nombre en un
  `Dictionary` propio del worker, así el `MQOPEN` se paga una única vez por
  cola. Un `ReplyToQ` inválido se loguea y no tumba el proceso.
- **Echo sin conversiones.** El payload se lee como `byte[]` y se escribe tal
  cual, copiando `Format`, `CharacterSet` y `Encoding` del pedido. No se pasa
  por `string`, así no hay costo de encoding ni riesgo de alterar el contenido.
  Si se configura `ReplyMessage`, se codifica a UTF-8 **una vez** en el
  constructor y se reutiliza el mismo buffer en cada respuesta.
- **Opciones MQ.** `MQGMO_WAIT | NO_SYNCPOINT | FAIL_IF_QUIESCING` en el GET;
  `MQPMO_NO_SYNCPOINT | NEW_MSG_ID` en el PUT; respuesta `MQMT_REPLY`, no
  persistente, `CorrelId = MsgId` del pedido. Valores tomados del cliente
  emisor de referencia para que el matching funcione sin cambios del otro lado.
- **`WaitInterval` finito (5 s) en vez de `MQWI_UNLIMITED`.** No agrega latencia
  cuando hay mensajes; solo acota cuánto tarda Ctrl+C en cortar el loop.
- **Hilos.** Con `WorkerThreads = 1` corre en el hilo principal, sin hilos
  extra. Con N > 1 se crean N `Thread` dedicados (no del ThreadPool, porque
  viven bloqueados en `MQGET` toda la ejecución). **Cada worker tiene su propia
  conexión** al manager y sus propios handles: no comparten nada, así el hot
  loop no necesita locks ni `Interlocked` (el contador de procesados es por
  worker y se suma al cerrar). Esto también respeta la regla del cliente de
  IBM: un `MQQueueManager` no es seguro para compartir entre hilos sin
  serializar las llamadas.
- **ThreadPool dimensionado.** `SetMinThreads(ProcessorCount + WorkerThreads)`
  para worker e I/O. Los workers no usan el pool, pero el cliente MQ y el
  runtime sí para tareas auxiliares; dimensionarlo evita que el pool tenga que
  crecer bajo carga (crece de a ~1 hilo cada 500 ms, y eso se ve como
  latencia).
- **Runtime.** `TieredPGO` activado; GC workstation no concurrente (menos
  jitter para pocos hilos). **No** se usa `InvariantGlobalization`: el
  inicializador estático de `IBM.WMQ.MQMessage` instancia la cultura `en-US`
  y explota con ese flag.
- **Sin AOT** en este proyecto, a propósito: es la variante JIT de la
  comparación.

### Cliente de prueba (`src/TestClient`)

- Reusa `MqSettings.cs` del replier (linkeado en el `.csproj`) para que ambos
  lean la misma forma de configuración.
- Cada hilo emisor (`SenderWorker`) tiene su propia conexión y hace
  put → get por `CorrelId` de a un mensaje, guardando latencias en un array
  local sin sincronización.
- Una `Barrier` arranca el reloj de pared cuando **todos** los hilos terminaron
  su warm-up, así el throughput no incluye el JIT ni el primer `MQOPEN`.
- Métricas: `avg/min/p50/p90/p99/max` de latencia (agregadas sobre todos los
  hilos), tiempo de pared y throughput en msg/s. Un timeout o un echo
  incorrecto cuentan como error y hacen que el proceso salga con código 1.

## Resultados de referencia

2000 mensajes por corrida, manager en `192.168.0.31` por LAN (ping < 1 ms),
replier .NET 10 JIT.

| Workers | Concurrencia | msg/s | p50 (ms) | p90 (ms) | p99 (ms) |
|---|---|---|---|---|---|
| 1 | 1 | 863 | 1.00 | 1.56 | 2.99 |
| 1 | 8 | 1947 | 2.72 | 7.14 | 16.4 |
| 1 | 32 | 2958 | 9.91 | 14.2 | 20.8 |
| 4 | 1 | 852 | 1.00 | 1.49 | 2.97 |
| 4 | 8 | ~1900 | 3.0–3.4 | 6.0–7.3 | 13–17 |
| 4 | 32 | 2253 | 12.9 | 19.1 | 30.1 |

Lectura:

- **El cuello de botella es el queue manager, no el replier ni la red.** Con un
  mensaje en vuelo el round-trip es ~1 ms mientras el RTT de red es < 1 ms
  para 4 saltos TCP; con concurrencia 32 el throughput sube a ~3000 msg/s sin
  saturar la LAN. El replier procesa cada mensaje en microsegundos y el resto
  del tiempo espera en `MQGET`.
- **Más workers no ayudan en este entorno**; a concurrencia 32 incluso empeoran
  (más conexiones compitiendo por la misma cola y el mismo canal en el
  manager). Los workers extra solo servirían si el replier tuviera trabajo real
  por mensaje.
- **Implicancia para la comparación de lenguajes:** el p50 y el throughput van
  a ser prácticamente iguales entre .NET JIT, AOT y Rust porque el techo lo
  pone MQ. Las diferencias esperables están en tiempo de arranque, primer
  mensaje (JIT), memoria residente y estabilidad del p99 (GC).

## Optimizaciones evaluadas y no aplicadas

Se listan para no volver a analizarlas desde cero. Ninguna cambia el hecho de
que el manager domina la latencia.

| Idea | Impacto estimado | Por qué no se hizo |
|---|---|---|
| `MQPMO_ASYNC_RESPONSE` en el PUT de respuesta | Alto (ahorra medio round-trip) | Los errores del PUT dejan de ser sincrónicos (`MQSTAT`); se decidió mantener el escenario simple |
| Read-ahead en la cola de entrada | Alto bajo carga, nulo con 1 en vuelo | Con varios workers cada conexión acapara mensajes y empeora el p99; requiere `SHARECNV ≥ 1` |
| `SHARECNV(1)` en el canal | Medio | Es configuración del manager, no del código |
| Prioridad de hilo/proceso | Solo p99 | No se pidió |
| ReadyToRun | Solo arranque y primeros mensajes | Se reserva para comparar con AOT |
| Reutilizar `MQMessage` / evitar allocations | Bajo (µs) | Invisible frente a 1 ms de round-trip |
| Loopback (`127.0.0.1`) | Marginal | El manager está en otro host; la red aporta < 0.3 ms |
| Bindings (memoria compartida) | Alto | Requiere el cliente MQ nativo instalado en el host del manager y otra librería .NET; cambia el escenario del benchmark |

## Rust y otros lenguajes

El protocolo de canal de MQ es propietario y solo tiene implementaciones
completas en Java y en el cliente managed de .NET. Cualquier otro lenguaje
(Rust, Go, Python, Node) accede vía FFI a la biblioteca C de IBM (`mqic`):

- **Rust**: crate `mqi` o bindings propios con `bindgen` sobre `cmqc.h`.
- **Go**: `mq-golang` (oficial de IBM, cgo).

Para no instalar MQ completo se puede usar el **cliente redistribuible**
(`IBM-MQC-Redist-Win64.zip`, IBM Fix Central): se descomprime junto al
ejecutable, sin instalador ni registro. Nota para la comparación: en ese caso
Rust usaría el cliente C de IBM, que es un stack distinto al managed de .NET;
para que sea pareja, la versión .NET podría correr sobre la misma DLL nativa
(`amqmdnet.dll` no-managed).

Alternativas descartadas: AMQP/MQTT (otro protocolo, no equivalente) y REST
(`mqweb`, decenas de ms de latencia).
