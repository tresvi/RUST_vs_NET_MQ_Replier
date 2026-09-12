# 0002 — Cliente managed, transporte TCP, sin bindings

**Estado:** Aceptado — 2026-09-12

## Contexto

`IBMMQDotnetClient` trae dos implementaciones: la **managed**
(`amqmdnetstd.dll`, protocolo de canal reimplementado en C#, no requiere
instalar MQ) y la **no-managed** (`amqmdnet.dll`, wrapper sobre el cliente C
`mqic`, requiere instalación de MQ y habilita el transporte *bindings* por
memoria compartida cuando el proceso corre en el host del manager).

El manager de referencia está en otro host (`192.168.0.31`).

## Decisión

Cliente managed con `TRANSPORT_MQSERIES_MANAGED` (TCP). Ningún requisito de
instalación en la máquina del replier ni del cliente de prueba.

## Alternativas

- **Bindings**: elimina canal y serialización TCP, latencia por salto de
  decenas de µs. Requiere correr en el host del manager con MQ instalado y
  la librería no-managed; cambia el escenario del benchmark y no está
  disponible en el entorno actual.
- **Loopback** (`127.0.0.1`): mejora marginal (la red aporta < 0.3 ms de los
  ~1 ms de round-trip) y compite por CPU con el manager. Descartado.

## Consecuencias

- Reproducible en cualquier máquina con el SDK de .NET.
- `InvariantGlobalization` no puede usarse: `IBM.WMQ.MQMessage` instancia la
  cultura `en-US` en su constructor estático.
- **Para las variantes en otros lenguajes**: el protocolo de canal es
  propietario y solo está implementado en Java y en el managed de .NET.
  Rust (`mqi`/bindgen) y Go (`mq-golang`) usan la biblioteca C de IBM vía
  FFI; para no instalar MQ se puede usar el cliente redistribuible
  (`IBM-MQC-Redist`). Eso introduce una diferencia de stack de cliente ajena
  al lenguaje; si se quiere una comparación pareja, la variante .NET puede
  correr sobre `amqmdnet.dll` (no-managed) contra la misma DLL C.
- **AOT**: el paquete no declara compatibilidad con trimming/AOT; la variante
  AOT debe validarse en runtime antes de darse por viable.
