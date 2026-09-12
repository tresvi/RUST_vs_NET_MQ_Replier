# Entorno IBM MQ

## Manager de referencia (MQGD)

El benchmark se corre contra un queue manager existente:

| Parámetro | Valor |
|---|---|
| Nombre | `MQGD` |
| Host / puerto | `192.168.0.31` / `1414` |
| Canal | `CHANNEL1` (SVRCONN) |
| Autenticación | Ninguna (el cliente no envía usuario ni password) |
| Cola de pedidos | `BNA.TU5.PEDIDO` |
| Cola de respuestas | `BNA.TU5.RESPUESTA` |

> Los atributos exactos de MQGD (`SHARECNV`, `MAXDEPTH`, `CHLAUTH`, `CONNAUTH`)
> **no fueron relevados**. Si tenés acceso a `runmqsc MQGD`, completá esta tabla
> con la salida de:
>
> ```
> DISPLAY QMGR CHLAUTH CONNAUTH
> DISPLAY CHANNEL('CHANNEL1') SHARECNV MCAUSER
> DISPLAY QLOCAL('BNA.TU5.*') MAXDEPTH DEFPSIST
> ```
>
> `SHARECNV` en particular afecta la latencia (ver ADR 0005).

## Manager local con Docker

Para desarrollar o medir sin depender de MQGD:

```bash
docker compose -f docker/docker-compose.yml up -d
```

Levanta la imagen oficial `icr.io/ibm-messaging/mq` con un manager `MQGD`,
listener en `1414` y ejecuta [`docker/mq/20-config.mqsc`](../docker/mq/20-config.mqsc)
al arrancar, que define:

- `BNA.TU5.PEDIDO` y `BNA.TU5.RESPUESTA` (`DEFPSIST(NO)`, `MAXDEPTH(100000)`).
- Canal `CHANNEL1` SVRCONN con `SHARECNV(1)` y `MCAUSER('app')`.
- `CHLAUTH` y `CONNAUTH` deshabilitados y permisos de `PUT/GET` para `app`.
  **Es una configuración de desarrollo**, no la de un manager real.

Después, en los dos `appsettings.json` poner `"ServerIp": "localhost"`.

Verificación rápida:

```bash
docker exec mqgd bash -c 'echo "DISPLAY QLOCAL(BNA.TU5.*) CURDEPTH" | runmqsc MQGD'
```

Consola web en `https://localhost:9443/ibmmq/console` (usuario `admin`,
password `passw0rd`).

## Diagnóstico

| Síntoma | Causa probable |
|---|---|
| `Reason=2538` (`MQRC_HOST_NOT_AVAILABLE`) al arrancar | No hay listener en `ServerIp:ServerPort` |
| `Reason=2035` (`MQRC_NOT_AUTHORIZED`) | El canal exige credenciales o `CHLAUTH` bloquea al usuario; cargar `User`/`Password` o ajustar el manager |
| `Reason=2085` (`MQRC_UNKNOWN_OBJECT_NAME`) | La cola no existe (o el `ReplyToQ` del pedido apunta a una cola inexistente) |
| `Reason=2033` en el TestClient (`TIMEOUT sin respuesta`) | El replier no está corriendo, o respondió a otra cola |
| Mensajes acumulados en `BNA.TU5.RESPUESTA` | Un cliente murió sin consumir sus respuestas; vaciar con `CLEAR QLOCAL` |
| Aviso `"dspmqver" no se reconoce...` | Inofensivo: la librería de IBM busca una instalación local de MQ |
