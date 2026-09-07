# Memory - Sesión 07/09/2026

## Rediseño UI Blazor

### Decisiones de diseño
- **Tema:** modo oscuro forzado.
- **Paleta:** azul profesional (fintech).
- **Estrategia:** mantener Bootstrap 5 y aplicar un tema propio mediante CSS custom properties en `app.css`; sin nuevas dependencias.

### Cambios principales
- Nuevo sistema de tokens CSS (`--pt-bg`, `--pt-surface`, `--pt-primary`, etc.) en `wwwroot/app.css`.
- Override completo de Bootstrap para modo oscuro: cards, modales, tablas, formularios, botones, alertas, dropdowns.
- Fuente `Inter` cargada desde Google Fonts en `App.razor`; `lang="es"` y clase `pt-dark` en `<html>`.
- Header moderno en `MainLayout.razor` con brand, selector de portfolio y botón de importar.
- Dashboard (`Home.razor`) remodelado:
  - Hero de KPI cards (valor total, ganancia/pérdida, TIR XIRR, mejor posición).
  - Rentabilidad por periodo en grid de tarjetas.
  - Gráfico de evolución con header limpio y pills de periodo.
  - Tabla de posiciones con menú desplegable de acciones (sustituye los 5 botones en fila).
  - Empty state mejorado.
- Gráficos ajustados al tema oscuro:
  - `PortfolioChart.razor`: colores `#22c55e` / `#ef4444`, grid y texto en tonos oscuros.
  - `AllocationChart.razor`: paleta moderna.
  - `charts.js`: leyendas, tooltip y borde del doughnut adaptados.
- Diálogos rediseñados: `AddItemDialog`, `EditItemDialog`, `SafeBackDialog`, `TransferDialog`.
- Textos en español unificados (antes había mezcla de inglés/español).

### Nuevos componentes placeholder
- `PortfolioSelector.razor`: dropdown para cambiar entre portfolios (solo "Mi portfolio" activo; "Nuevo portfolio" deshabilitado).
- `ImportButton.razor`: dropdown con opciones CSV y PDF.
- `ImportDialog.razor`: modal "Próximamente" para importar CSV/PDF.

### Archivos modificados/creados
- Modificados: `App.razor`, `wwwroot/app.css`, `Components/Layout/MainLayout.razor`, `Components/Layout/MainLayout.razor.css`, `Components/Pages/Home.razor`, `Components/AllocationChart.razor`, `Components/PortfolioChart.razor`, `wwwroot/charts.js`, `Components/Dialogs/AddItemDialog.razor`, `Components/Dialogs/EditItemDialog.razor`, `Components/Dialogs/SafeBackDialog.razor`, `Components/Dialogs/TransferDialog.razor`.
- Creados: `Components/Layout/PortfolioSelector.razor`, `Components/Layout/ImportButton.razor`, `Components/Dialogs/ImportDialog.razor`.

### Verificación
- `dotnet build PortfolioTracker.slnx` → OK.
- `dotnet test tests/PortfolioTracker.Tests` → 19 tests OK.
- Revisión visual local pendiente (no se pudo arrancar la API + Blazor en el entorno del agente).

### Actualización de AGENTS.md
- Se ha actualizado `AGENTS.md` para reflejar con precisión cómo arrancar API y Blazor juntos, incluyendo el uso de `ApiBaseUrl` cuando el API no corre en el puerto por defecto.
- Añadida sección "UI Theme" para recordar que el tema oscuro se basa en tokens CSS y no se deben añadir nuevas librerías de UI sin consenso.

### Pendiente / próximos pasos
- Revisar visualmente en local con datos reales.
- Ajustar detalles finos de espaciado/colores tras la revisión visual.
- Cuando se implemente la carga de múltiples portfolios, conectar `PortfolioSelector` con la API.
- Cuando se implemente importación CSV/PDF, reemplazar `ImportDialog` por flujo real.

---

# Memory - Sesión 04/09/2026

## Preferencias del usuario

- **Idioma de respuesta:** español de España (no rioplatense).

## Bugs corregidos

### Recompra de fondo Cobas (error de Entity Framework)
- **Causa:** en `PortfolioService.AddItemAsync`, la rama de merge (`existingItem != null`) asignaba `request.PurchaseDate` sin forzar `DateTimeKind.Utc`. Npgsql rechazaba escribir un `DateTime` con `Kind=Unspecified` en `timestamp with time zone`.
- **Fix:** aplicar `DateTime.SpecifyKind(..., DateTimeKind.Utc)` también en la rama de merge.
- **Archivo:** `src/PortfolioTracker.Api/Services/PortfolioService.cs`

### Normalización de símbolos de fondos mutuos
- **Problema:** Yahoo devuelve símbolos de fondos (`0P*`) a veces con sufijo de exchange (`.F`, `.DE`) y a veces sin él, generando duplicados o fallos de merge.
- **Fix:** `PortfolioService.NormalizeSymbol` quita el sufijo para símbolos que empiezan con `0P`, centralizando la clave del índice único `(UserId, Symbol)`.
- **Archivo:** `src/PortfolioTracker.Api/Services/PortfolioService.cs`

### Tildes y eñes en la UI
- **Problema:** se usaban entidades HTML (`&oacute;`, `&ntilde;`, etc.) en atributos `title` y markup de Blazor, que no se renderizaban correctamente.
- **Fix:** reemplazo masivo por caracteres UTF-8 directos en todos los `.razor`.
- **Archivos:** `Home.razor`, `AddItemDialog.razor`, `TransferDialog.razor`, `PortfolioChart.razor`, `SafeBackDialog.razor`

## Features implementadas

### Añadir compra a inversión existente
- `AddItemDialog` ahora tiene dos modos: "Nueva inversión" (búsqueda) y "Añadir a existente" (dropdown de posiciones actuales).
- Botón rápido `+` en cada fila del dashboard para abrir el diálogo en modo existente.
- **Archivos:** `AddItemDialog.razor`, `Home.razor`

### SafeBack de Trade Republic
- Modelo: `SafeBackAmount` y `SafeBackShares` en `PortfolioItem`.
- Endpoint: `POST /api/portfolio/{userId}/safeback`.
- Servicio: `TransactionService.AddSafeBackAsync`.
- Diálogo `SafeBackDialog` y botón circular en cada fila del dashboard.
- **Corrección conceptual durante la sesión:** inicialmente se modeló como shares "gratis" (sin aumentar cost basis). Tras feedback del usuario, se corrigió para que sea una **compra a precio de mercado con el dinero recibido**, aumentando el cost basis como una `Buy` normal pero trackeando el importe recibido como SafeBack.
- **Archivos:** `PortfolioItem.cs`, `PortfolioDbContext.cs`, `TransactionService.cs`, `Program.cs`, `SafeBackDialog.razor`, `Home.razor`, `PortfolioApiService.cs`, `PortfolioModels.cs`

## Migraciones

- `20260904113632_AddSafeBack`: añade columnas `SafeBackAmount` (numeric 18,2) y `SafeBackShares` (numeric 18,8) a `PortfolioItems`.
- Se aplica automáticamente al iniciar la API (`db.Database.Migrate()`).

## Skills y convenciones

- Creado skill global `session-context-recall` en `.config/opencode/skills/session-context-recall/SKILL.md` para que cada agente lea `notes/memory.md` y la carpeta `notes/` al iniciar sesión.
- Registrada la regla en `AGENTS.md` del proyecto (sección "Session Context").
- Reescrito `AGENTS.md` para que sea compacto, esté al día y recoja reglas de alto valor (SafeBack, `dotnet-ef` local, `UserId` fijo, cultura invariante, etc.).

## Scripts

- `scripts/rebuild_gold.sql`: reconstruye la inversión de oro con las aportaciones y SafeBacks indicados por el usuario.
  - Aportaciones propias: 5 × 50 € = 250 €
  - SafeBack acumulado: 31,55 €
  - Shares totales: 3,91342593
  - Precio medio: 71,9446 €
  - Requiere reemplazar `:user_id`, `:symbol` y `:name` antes de ejecutar.

## Tests

- Añadidos tests para `NormalizeSymbol` y campos SafeBack en `PortfolioCalculationsTests`.
- Añadido `src/PortfolioTracker.Api/Properties/AssemblyInfo.cs` con `InternalsVisibleTo("PortfolioTracker.Tests")`.
- Resultado: 19 tests OK.

## Herramientas

- Añadido `dotnet-tools.json` con `dotnet-ef 10.0.10` (local) porque la versión global instalada (9.0.10) era incompatible con EF Core 10.

## Comandos de verificación

```bash
dotnet build PortfolioTracker.slnx
dotnet test tests/PortfolioTracker.Tests
```

## Pendiente / próximos pasos

- Ejecutar `scripts/rebuild_gold.sql` contra PostgreSQL con el `UserId` y símbolo correctos.
- Revisar visualmente que el diálogo SafeBack y el modo "Añadir a existente" funcionen con datos reales.
- Considerar alinear `Npgsql.EntityFrameworkCore.PostgreSQL` a `10.0.10` para eliminar warnings de unificación de ensamblados.
