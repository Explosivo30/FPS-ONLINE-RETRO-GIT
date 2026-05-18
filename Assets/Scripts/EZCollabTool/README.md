# EZCollabTool

Versión: 0.1.0
Unity: 6
Plataforma: Editor only (Windows / macOS / Linux)

Plugin de colaboración en tiempo real para Unity. Varios editores trabajan sobre la misma escena simultáneamente sin servicios externos, sin suscripciones y sin que nadie excepto el host tenga que tocar su router.

---

## Cómo funciona

Un miembro del equipo actúa como **host**. Su Unity arranca un servidor WebSocket embebido dentro de su propio proceso — no hay ningún programa separado que instalar. Los demás se conectan como **clientes** hacia la IP del host. Ningún cliente necesita abrir puertos porque todas las conexiones las inician ellos hacia el host, y el router las deja pasar en los dos sentidos por ser la misma conexión TCP.

Al conectarse, cada cliente recibe un **snapshot completo** de la escena activa del host y la reconstruye localmente desde cero. A partir de ahí los cambios viajan como deltas pequeños en tiempo real.

El host es la única fuente de verdad. Los clientes nunca se hablan entre ellos directamente — todo pasa por el host, que actúa de árbitro.

### Locks

Los locks se piden en el momento de **seleccionar** un objeto, no al empezar a moverlo. Si otro editor ya lo tiene seleccionado, el tuyo aparece con un borde rojo en la SceneView y no puedes modificarlo. Cuando lo deseleccionas, el lock se libera automáticamente y los demás pueden cogerlo.

Si el editor que tenía el lock se desconecta, sus locks se liberan solos en el servidor.

### Rendimiento con escenas grandes

Cada objeto recibe un GUID al indexarse. Cuando llega un delta (mover, rotar, escalar, crear, borrar), el lookup del objeto destino es una búsqueda en un `Dictionary<string, GameObject>` — O(1) sin importar el tamaño de la escena. No hay ningún `Find()` ni recorrido de jerarquía por cada cambio recibido. Una escena con 50.000 objetos hace el mismo lookup que una con 10.

El único momento donde el tamaño de la escena importa es el snapshot inicial al conectarse un nuevo cliente. Escenas muy grandes tardarán unos segundos en serializarse y enviarse — es puntual y solo ocurre una vez por conexión.

Los deltas de transform se envían a 20hz (cada 50ms) mientras mueves, no por frame. El receptor interpola, por lo que el movimiento se ve suave aunque los mensajes lleguen a esa frecuencia.

### Git

El plugin no toca Git en ningún momento. Guarda la escena a disco periódicamente (igual que Ctrl+S), pero `git add`, `git commit` y `git push` son siempre decisión tuya. Desde el punto de vista de Git, es como si simplemente hubieras guardado la escena a mano.

---

## Setup

### Requisito previo para todos

Todos deben estar en la misma rama y haber hecho `git pull` antes de conectarse. El plugin sincroniza estado de escena, no archivos. Si alguien tiene assets que otros no tienen, los objetos que los usen aparecerán con material rosa (missing) en los que no los tienen.

### Host

1. Abrir `Window → EZCollabTool`
2. Escribir tu nombre
3. Dejar el puerto en 9876 o cambiarlo si ya lo tienes ocupado
4. Pulsar **Start session**
5. Compartir tu IP pública con el equipo (puedes buscarla en [whatismyip.com](https://www.whatismyip.com))
6. En tu router, crear una regla de port forwarding: protocolo TCP, puerto externo 9876, puerto interno 9876, IP destino = tu IP local (la que empieza por 192.168.x.x o 10.x.x.x)

Si tu ISP te da IP dinámica y cambia entre sesiones, considera un servicio de DDNS gratuito como [Duck DNS](https://www.duckdns.org) para tener siempre la misma dirección.

### Clientes

1. Abrir `Window → EZCollabTool`
2. Escribir tu nombre
3. Escribir la IP pública del host y el puerto
4. Pulsar **Join session**

Los clientes no tocan nada de su router.

### LAN o Tailscale (alternativa sin port forwarding)

Si el equipo está en la misma red local o usa [Tailscale](https://tailscale.com) (gratuito hasta 100 dispositivos), el host no necesita abrir ningún puerto. Los clientes usan la IP local del host directamente (192.168.x.x o la IP de Tailscale). La latencia en LAN es de 1-5ms.

---

## Instalación del package

En Unity: `Window → Package Manager → + → Add package from disk` y selecciona el `package.json` de esta carpeta.

Dependencia única: `com.unity.nuget.newtonsoft-json`, que en Unity 6 ya viene disponible. Si el Package Manager no la resuelve automáticamente, instálala manualmente desde el registry.

---

## Qué se sincroniza

| Acción | Se sincroniza |
|---|---|
| Mover / rotar / escalar | Sí |
| Crear primitiva | Sí |
| Instanciar prefab (si todos lo tienen) | Sí |
| Borrar objeto | Sí |
| Cambiar nombre de objeto | No (v0.1) |
| Cambiar componentes | No (v0.1) |
| Importar assets nuevos | No — requiere push/pull |
| Cambios en materiales | No (v0.1) |

---

## Limitaciones conocidas

**Si el host cierra Unity, la sesión muere para todos.** Los clientes mantienen su estado local de la escena, pero la sesión en red termina. El host debería avisar antes de cerrar.

**Los assets nuevos importados durante la sesión no se ven en los demás** hasta que el host hace push y el resto hace pull y reconecta.

**No hay undo global colaborativo.** El undo de Unity deshace tus propios cambios localmente. No hay forma de deshacer lo que hizo otra persona desde tu editor.

**Conflicto de último frame.** Si dos personas seleccionan el mismo objeto en el mismo instante (antes de que el lock del primero llegue al segundo), gana quien llegó primero al host. El segundo recibe un LockDenied y el objeto se deselecciona. Puede verse como un snap puntual — es el comportamiento correcto y esperado.

---

## Arquitectura resumida

```
Editor A (host)
  └─ EZCollabServer (WebSocket, :9876)
       ├─ EZCollabLockManager (tabla de locks en memoria)
       └─ broadcast de deltas a clientes conectados

Editor B / C / N (clientes)
  └─ EZCollabClient (WebSocket saliente → host)

EZCollabHooks (InitializeOnLoad)
  ├─ Selection.selectionChanged → pedir/liberar locks
  ├─ hierarchyChanged → detectar objetos nuevos
  └─ Undo.postprocessModifications → enviar deltas de transform a 20hz

EZCollabOverlay (SceneView)
  └─ dibuja bordes de lock sobre objetos bloqueados

EZCollabState
  └─ Dictionary<guid, GameObject> — lookup O(1), sin Find()
```
