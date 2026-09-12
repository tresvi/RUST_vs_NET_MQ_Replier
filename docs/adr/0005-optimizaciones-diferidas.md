# 0005 — Optimizaciones evaluadas y diferidas

**Estado:** Diferido — 2026-09-12

## Contexto

Tras la primera medición (ver `docs/benchmarks.md`) quedó claro que el
round-trip (~1 ms con un mensaje en vuelo) lo domina el manager: el replier
procesa en microsegundos y espera el resto. Se evaluó qué más se podía
recortar desde el replier. Se decidió **no aplicar** nada de esto por ahora
para mantener la variante .NET JIT como línea base simple y comparable; este
ADR deja el análisis para no repetirlo.

## Opciones, por impacto estimado

| Opción | Impacto | Costo / riesgo |
|---|---|---|
| `MQPMO_ASYNC_RESPONSE` en el PUT de la respuesta | **Alto**: el PUT no espera confirmación del manager; ahorra medio round-trip (15–25 % del tiempo del replier). Uso previsto para no persistente + no syncpoint | Los errores del PUT dejan de ser sincrónicos; hay que consultarlos con `MQSTAT` |
| Read-ahead en la cola de entrada (`MQOO_READ_AHEAD` o `DEFREADA(YES)`) | **Alto bajo carga**, nulo con 1 en vuelo: el manager empuja mensajes al buffer del cliente | Con varios workers cada conexión acapara un lote y empeora el p99; requiere `SHARECNV ≥ 1`; los mensajes en el buffer se pierden si el cliente muere (aceptable: no persistentes) |
| `SHARECNV(1)` en el canal SVRCONN | Medio (5–15 % en latencia según IBM) | Es configuración del manager, no del código |
| `ThreadPriority.AboveNormal` / `ProcessPriorityClass.High` | Solo p99/max (picos de 10–30 ms del scheduler) | Barato; puede afectar a otros procesos del host |
| `PublishReadyToRun` | Solo arranque y primeros mensajes (~45 ms del primer round-trip) | Ninguno; se reserva para la comparación con AOT |
| Reutilizar `MQMessage` (`ClearMessage` + reset de `MessageId`/`CorrelId` a `NONE`) y evitar `TrimEnd` | Bajo (µs, algo menos de GC gen0) | Riesgo de olvidar el reset y que el `GET` filtre por `MsgId` |
| `MQGMO_NO_PROPERTIES` en el GET | Marginal | Ninguno |

## Decisión

Diferir todas. Si se retoma, el orden sugerido es: PUT asíncrono +
prioridad de hilo + ReadyToRun (solo cambios en el replier), y read-ahead
como opción configurable para probar con `WorkerThreads = 1`.

## Consecuencias

- La línea base .NET JIT es la implementación "directa"; las variantes AOT y
  Rust deben compararse contra ella sin estas optimizaciones, o aplicarlas en
  todas por igual.
