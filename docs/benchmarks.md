# Bitácora de benchmarks

Cada entrada: fecha, commit, entorno y tabla. La metodología está en
[`PRD.md`](PRD.md). Agregar entradas al final; no reescribir las anteriores.

---

## 2026-09-12 — .NET 10 JIT, primera medición

- Commit: `7296352`
- Replier: `src/NetMqReplier`, .NET SDK 10.0.302, `IBMMQDotnetClient 9.4.5.1`,
  Release, JIT (TieredPGO).
- Cliente: `src/TestClient`, mismo host que el replier.
- Host cliente: Windows 10 Pro 19045 (hardware no registrado).
- Manager: `MQGD` en `192.168.0.31:1414` por LAN, ping < 1 ms. Versión y
  atributos del canal no relevados.
- 2000 mensajes medidos por corrida, warm-up 10 por hilo, **una corrida por
  celda** (salvo donde se indica).

| Workers | Concurrencia | msg/s | p50 (ms) | p90 (ms) | p99 (ms) | max (ms) | Notas |
|---|---|---|---|---|---|---|---|
| 1 | 1 | 863 | 1.00 | 1.56 | 2.99 | 13.3 | |
| 1 | 8 | 1947 | 2.72 | 7.14 | 16.4 | 45.1 | |
| 1 | 32 | 2958 | 9.91 | 14.2 | 20.8 | 29.8 | |
| 4 | 1 | 852 | 1.00 | 1.49 | 2.97 | 24.5 | |
| 4 | 8 | 1798–1984 | 3.05–3.37 | 5.97–7.32 | 13.2–17.1 | 26.5–30.1 | 3 corridas; una corrida previa tuvo 1 timeout (5 s) no reproducido |
| 4 | 32 | 2253 | 12.9 | 19.1 | 30.1 | 41.2 | |

Métricas no medidas todavía: arranque, primer mensaje, memoria, CPU.
Observado informalmente: primer round-trip ~35–55 ms (JIT), régimen desde el
mensaje ~3.

Conclusiones:

- El manager es el cuello de botella; el replier está ocioso.
- 4 workers no mejoran sobre 1 y empeoran a concurrencia 32.
- Con 1 en vuelo, el round-trip mínimo es ~1 ms (4 saltos TCP + proceso del
  manager).
