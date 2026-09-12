# 0004 — Contrato request/reply: CorrelId, ReplyToQ, echo

**Estado:** Aceptado — 2026-09-12

## Contexto

El emisor existente manda pedidos no persistentes, `MQSTR`/CCSID 1208, con
`MessageId = NONE` + `MQPMO_NEW_MSG_ID` y `CorrelId = NONE`, y espera la
respuesta con `MQGET` filtrando por `CorrelId` igual al `MsgId` que obtuvo al
hacer el `PUT`. Las colas del escenario son `BNA.TU5.PEDIDO` y
`BNA.TU5.RESPUESTA`.

## Decisión

1. **Correlación**: la respuesta lleva `CorrelId = MsgId` del pedido,
   `MsgId` nuevo (`MQPMO_NEW_MSG_ID`), `MsgType = MQMT_REPLY`, no persistente,
   `NO_SYNCPOINT`.
2. **Destino**: `ReplyToQ` del MQMD del pedido si viene informado (con
   `TrimEnd()` del padding a 48 caracteres); si no, `Mq.ReplyQueue`. Los
   handles de colas de salida se cachean por nombre; un `ReplyToQ` inválido
   se loguea y el worker sigue.
3. **Cuerpo**: por defecto echo byte a byte (`ReadBytes` → `Write`) copiando
   `Format`, `CharacterSet` y `Encoding` del pedido, sin pasar por `string`.
   Si `Mq.ReplyMessage` está configurado, se responde ese texto como
   `MQSTR`/UTF-8, codificado una sola vez al arrancar.

## Alternativas

- **Cola de respuesta fija ignorando `ReplyToQ`**: más simple, pero rompe el
  patrón estándar de MQ y obliga a todos los emisores a usar la misma cola.
- **`CorrelId` del pedido copiado tal cual**: el emisor manda `NONE`, no
  serviría para hacer matching.
- **Echo vía `ReadString`/`WriteString`**: costo de conversión y riesgo de
  alterar bytes si el CCSID no coincide.

## Consecuencias

- Cualquier variante (AOT, Rust) debe reproducir exactamente estos tres
  puntos para que el mismo TestClient sirva y la comparación sea válida.
- Cambiar el contrato requiere coordinar con el emisor.
