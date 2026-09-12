# PRD — Benchmark de repliers IBM MQ

## Pregunta que responde

¿Qué gana (o pierde) un servicio *replier* de IBM MQ al implementarse en
Rust o en .NET compilado AOT, frente a .NET con JIT, en un escenario
request/reply real contra un queue manager remoto?

## Contexto

En los sistemas donde se usa este patrón, un proceso escucha una cola de
pedidos y contesta a una cola de respuesta. La latencia de respuesta y el
consumo de recursos del proceso son los atributos que interesan. Existe la
hipótesis de que un lenguaje sin runtime gestionado (Rust) o un binario AOT
darían una mejora sustancial; este proyecto la pone a prueba con números.

## Alcance

**Dentro:**

- Replier equivalente en tres variantes: .NET 10 JIT (hecho), .NET 10 AOT,
  Rust.
- Un único generador de carga (`src/TestClient`) que sirve para las tres.
- Medición contra un manager remoto por LAN, con cliente TCP.

**Fuera:**

- Tuning del queue manager o de la red.
- Modo bindings (memoria compartida) y otros protocolos (AMQP, MQTT, REST).
- Lógica de negocio: el replier solo hace echo (o responde un texto fijo).

## Contrato funcional del replier

Todas las variantes deben cumplirlo para que la comparación sea válida:

1. Conectar al manager y abrir las colas **antes** de empezar a consumir.
2. Escuchar en `Mq.InputQueue` con `MQGET` bloqueante, sin syncpoint.
3. Responder a la cola indicada en `ReplyToQ` del MQMD; si viene vacía, a
   `Mq.ReplyQueue`.
4. Respuesta con `MsgType = MQMT_REPLY`, no persistente, `MsgId` nuevo y
   `CorrelId = MsgId` del pedido.
5. Cuerpo: echo byte a byte del pedido conservando `Format`, `CCSID` y
   `Encoding`; o, si `Mq.ReplyMessage` está configurado, ese texto como
   `MQSTR`/UTF-8.
6. Configuración con la misma forma (`Mq.*`: canal, manager, host, puerto,
   credenciales opcionales, colas, `WorkerThreads`, `ReplyMessage`).
7. `WorkerThreads` configurable; cada worker con su propia conexión.
8. Cierre ordenado con Ctrl+C.

## Métricas

Resultado ya conocido (ver README): con un manager remoto, **p50 y throughput
están dominados por MQ** y van a ser prácticamente iguales entre variantes.
Por eso las métricas primarias son las que sí dependen del proceso:

| Métrica | Cómo se mide | Por qué importa |
|---|---|---|
| Tiempo de arranque hasta "escuchando" | Timestamp de inicio del proceso → log "Escuchando" | Reinicios, escalado, cold start |
| Latencia del primer mensaje | Primer round-trip del TestClient (warm-up 0) | JIT vs precompilado |
| Memoria residente en régimen | Working set tras 2000 mensajes | Densidad de despliegue |
| p99 / max bajo carga | TestClient con concurrencia 8 y 32 | Jitter de GC / runtime |
| CPU del proceso | % CPU durante la corrida con concurrencia 32 | Costo por mensaje |

Secundarias, para confirmar que las variantes son equivalentes:
throughput y p50 en la misma matriz.

## Metodología

- Matriz: `WorkerThreads ∈ {1, 4}` × `Concurrency ∈ {1, 8, 32}`, 2000
  mensajes medidos, warm-up 10 por hilo.
- Cada celda al menos **3 corridas**; se reporta la mediana y se anota si hubo
  timeouts.
- Mismo host cliente, mismo manager, misma franja horaria para las tres
  variantes. Registrar hardware, versiones (SDK, NuGet, toolchain Rust,
  versión del manager) y commit.
- Los resultados se anotan en [`benchmarks.md`](benchmarks.md); el README solo
  lleva el resumen.

## Criterios de éxito del proyecto

- Las tres variantes pasan el TestClient con `errores: 0` en toda la matriz.
- Existe una tabla comparativa con las métricas primarias y una conclusión
  escrita sobre si la hipótesis se sostiene y en qué medida.

## Riesgos conocidos

- `IBMMQDotnetClient` no declara compatibilidad con AOT/trimming; la variante
  AOT puede requerir configuración de trimming o no ser viable (ADR 0002).
- Rust necesita la biblioteca C de IBM (cliente redistribuible); usa un stack
  de cliente distinto al managed de .NET, lo que introduce una variable ajena
  al lenguaje (ADR 0002).
- El manager de referencia es compartido y su configuración no está relevada
  (`docs/mq-setup.md`); variaciones de carga externa pueden afectar corridas.
