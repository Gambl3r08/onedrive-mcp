# onedrive-mcp

Servidor MCP (Streamable HTTP) para **OneDrive for Business**, en .NET 10, pensado
para registrarse en Flow Weaver como servidor MCP con autenticación por API key.

- Transporte: Streamable HTTP en `/mcp` (modo stateless). `/health` sin auth.
- Auth hacia Microsoft: permisos *delegados* de un usuario, login una sola vez por
  device code; el refresh token queda en el volumen `/data` y se renueva solo.
- Auth desde Flow Weaver: header `X-API-Key` (o `Authorization: Bearer`).
- Alcance: solo el OneDrive del usuario logueado (`/me/drive`). Sin herramienta de borrado.

## Herramientas

| Herramienta | Tipo | Descripción |
|---|---|---|
| `onedrive_status` | lectura | Drive, dueño, cuota |
| `onedrive_list` | lectura | Contenido de una carpeta |
| `onedrive_search` | lectura | Búsqueda por nombre/contenido |
| `onedrive_get_item` | lectura | Metadatos de un archivo/carpeta |
| `onedrive_read_text` | lectura | Lee archivos de texto (UTF-8, con tope de tamaño) |
| `onedrive_create_folder` | escritura | Crea una carpeta |
| `onedrive_upload_text` | escritura | Escribe un archivo de texto (sin sobrescribir salvo `overwrite`) |
| `onedrive_move_item` | escritura | Mueve y/o renombra |
| `onedrive_create_share_link` | escritura | Link `view`/`edit`, ámbito organización (anónimo solo si se habilita) |

Con `OneDrive__ReadOnly=true` solo se exponen las de lectura y el login pide `Files.Read`.

## 1. Registrar la app en Entra ID

1. Entra admin center -> **App registrations** -> **New registration**.
   - Nombre: `onedrive-mcp`. Cuentas: *solo este directorio organizativo*. Sin redirect URI.
2. **Authentication** -> *Advanced settings* -> **Allow public client flows = Yes** (necesario para device code).
3. **API permissions** -> Microsoft Graph -> *Delegated*: `User.Read`, `Files.ReadWrite`
   (o `Files.Read` si vas a usar modo solo lectura). Si el tenant exige consentimiento
   de admin, pulsar **Grant admin consent**.
4. Copiar **Directory (tenant) ID** y **Application (client) ID**.

No hace falta client secret: es un cliente público.

## 2. Configurar y levantar

```powershell
Copy-Item .env.example .env
# editar .env: TenantId, ClientId y una ApiKey generada con:
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

El compose se une a la red docker de Flow Weaver (`flow-weaver_flow-weaver-net`), que
debe existir (se crea al levantar FW). Si tu proyecto de FW tiene otro nombre, definí
`FLOW_WEAVER_NETWORK` en el entorno.
```powershell
docker compose up -d --build
docker compose run --rm onedrive-mcp login     # muestra URL + código; abrirlo y loguearse
docker compose run --rm onedrive-mcp status    # "Signed in as usuario@empresa.com."
Invoke-WebRequest http://127.0.0.1:8090/health | % Content   # signedIn: true
```

`login` comparte el volumen con el servicio, así que el servidor en marcha toma la sesión
sin reiniciar. `logout` borra la caché.

## 3. Registrar en Flow Weaver

**Govern -> MCP servers -> Add server**:

| Campo | Valor |
|---|---|
| Name | `OneDrive` |
| Server URL | `http://onedrive-mcp:8080/mcp` |
| Authentication | **API key (header)**, header `X-API-Key`, valor = `OneDrive__ApiKey` |
| Allow private / loopback network | **activado** (la IP del contenedor es 172.x) |
| Skip TLS verification | no aplica (HTTP interno) |

Luego **Test & sync tools** -> status `ok` y 9 herramientas (5 en modo solo lectura).
Para el agente de chat, el usuario necesita `mcp.read` y `mcp.execute` (este último se
puede restringir por servidor/herramienta con las condiciones `mcp_server`/`mcp_tool`).

Nota: FW bloquea `localhost`/loopback aunque se active *Allow private network*, por eso
la URL usa el nombre del contenedor en la red compartida y no `http://localhost:8090`.
Si FW corre fuera de docker (`dotnet run` en el host), usar la IP LAN de la máquina.

## 4. Registrar en Nashira

El contenedor también se une a `nashira_nashira-net` (variable `NASHIRA_NETWORK`).
En Nashira (MCP servers -> Add) los mismos valores: URL `http://onedrive-mcp:8080/mcp`,
auth `api_key` con header `X-API-Key`, *Allow private network* activado; luego **Sync**.
Por API: `POST /api/mcp/servers` con `auth_config: {api_key_header, api_key}`, luego
`POST /api/mcp/servers/{id}/sync`. `POST /api/mcp/servers/{id}/call` prueba una
herramienta sin pasar por el agente.

Opcional: *Trust tool hints* hace que Nashira use los `readOnlyHint` del servidor, de
modo que las 5 herramientas de lectura se ejecutan sin pedir confirmación.

Compatibilidad: FW y Nashira usan `ModelContextProtocol.Core 2.0.0-preview.3`; el
servidor está fijado a esa versión (ver comentario en el `.csproj`).

## Prueba manual (sin FW)

```powershell
$h=@{ "X-API-Key"="<api key>"; Accept="application/json, text/event-stream" }
Invoke-WebRequest http://127.0.0.1:8090/mcp -Method Post -Headers $h -ContentType application/json `
  -Body '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"onedrive_list","arguments":{"path":""}}}' | % Content
```

También sirve el MCP Inspector: `npx @modelcontextprotocol/inspector` -> Streamable HTTP,
URL `http://127.0.0.1:8090/mcp`, header `X-API-Key`.

## Seguridad

- La caché MSAL (`/data/msal_cache.bin`, modo 0600) contiene el refresh token en claro:
  quien tenga el volumen tiene acceso al OneDrive del usuario. No montarlo en otros servicios.
- El puerto del host se publica solo en `127.0.0.1`.
- Las herramientas actúan con los permisos del usuario que hizo `login`; usar una cuenta
  de servicio dedicada si se quiere acotar a qué carpetas llega.
