# 0003 — Una conexión por worker, sin estado compartido

**Estado:** Aceptado — 2026-09-12

## Contexto

Se pidió que el replier pudiera escalar a N hilos por configuración,
manteniendo la latencia como prioridad. El cliente de IBM documenta que un
`MQQueueManager` no es seguro para uso concurrente sin serializar las
llamadas.

## Decisión

Cada `ReplierWorker` abre **su propia** `MQQueueManager`, su handle de cola de
entrada y su cache de colas de salida. No hay locks, `Interlocked` ni
colecciones compartidas en el hot loop; el contador de procesados es por
worker y se suma al cerrar. Los workers corren en `Thread` dedicados (no del
ThreadPool) porque viven bloqueados en `MQGET`. Con `WorkerThreads = 1` el
loop corre en el hilo principal, sin hilos extra.

El ThreadPool se dimensiona con `SetMinThreads(ProcessorCount + WorkerThreads)`
porque el cliente MQ y el runtime lo usan para tareas auxiliares y su
crecimiento bajo demanda (~1 hilo cada 500 ms) se vería como latencia.

El mismo patrón se aplica al `SenderWorker` del TestClient.

## Alternativas

- **Una conexión compartida con `MQCNO_HANDLE_SHARE_BLOCK`**: el cliente
  serializa las llamadas; los hilos se bloquean entre sí en cada
  `MQGET`/`MQPUT`. Anula el paralelismo.
- **Tareas en el ThreadPool / `async`**: `MQGET` es bloqueante; ocuparía hilos
  del pool indefinidamente y agregaría el scheduler al camino crítico.

## Consecuencias

- N conexiones de canal al manager por proceso. En el entorno actual, con N=4
  el throughput a concurrencia 32 fue **peor** que con N=1 (contención en el
  manager sobre la misma cola/canal). El default es `WorkerThreads = 1`.
- Los workers extra solo tienen sentido si el replier hace trabajo real por
  mensaje.
- Cada worker paga su propio warm-up de JIT.
