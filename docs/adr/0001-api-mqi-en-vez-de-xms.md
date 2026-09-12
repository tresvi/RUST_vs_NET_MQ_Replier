# 0001 — API MQI (`IBMMQDotnetClient`) en vez de XMS

**Estado:** Aceptado — 2026-09-12

## Contexto

IBM ofrece dos APIs .NET para hablar con MQ:

- **MQI** (`IBMMQDotnetClient`): `MQQueueManager`, `MQQueue`, `MQMessage`,
  opciones de `MQGET`/`MQPUT` bit a bit, acceso directo al MQMD.
- **XMS** (`IBMXMSDotnetClient`): API con la semántica de JMS (sesiones,
  destinos, `ITextMessage`, `JMSCorrelationID`, listeners). Por debajo usa la
  misma `amqmdnetstd.dll`.

El replier prioriza latencia y debe interoperar con un emisor que trabaja con
MQMD "pelado" (`MQSTR`, CCSID 1208, `CorrelId = NONE`, matching por
`MATCH_CORREL_ID`).

## Decisión

Usar MQI.

## Alternativas

- **XMS**: agrega una capa (mapeo JMS ↔ MQMD, cabecera MQRFH2 por defecto,
  objetos de sesión) sobre el mismo cliente; en el mejor caso iguala a MQI y
  normalmente cuesta más. Requiere `XMSC_WMQ_TARGET_CLIENT = MQ` para no
  adjuntar RFH2 y respetar el formato `ID:hex` del `JMSCorrelationID`. Los
  selectores se evalúan en el manager y son más lentos que
  `MATCH_CORREL_ID`.

## Consecuencias

- Control total de `Format`, `CharacterSet`, `Encoding`, `Persistence`,
  `CorrelId`, `ReplyToQ` y de las opciones `NO_SYNCPOINT`,
  `FAIL_IF_QUIESCING`, `NEW_MSG_ID`.
- Sin consumo asíncrono ni transacciones de sesión; no se necesitan.
- MQI es el mismo modelo que la API C de IBM, lo que hace comparable el
  código con las variantes Rust/Go.
