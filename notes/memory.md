# Memory - Sesión 11/09/2026

## Correcciones de la auditoría (F1–F7 y backlog)

### Dinero y métricas
- **F1 SafeBack:** `TransactionService.AddSafeBackAsync` ahora diluye `PurchasePrice` con `PortfolioService.ComputeSafeBackPrice` (añade participaciones sin coste). `Shares * PurchasePrice` queda constante → la rentabilidad ya no se hunde. Tests añadidos.
- **TWR con flujos en huecos:** `ReturnCalculators.SumFlowsBetween` suma los flujos del intervalo `(punto previo, punto actual]`, así una compra en fin de semana/festivo ya no infla el TWR.
- **Comisiones y traspasos por posición:** `ComputeItemReturnSeries` usa `AmountEur + Commission` para Buy y resta TransferOut, igual que `ComputeSimpleReturnSeries`.
- **F5 una sola métrica por ventana:** `PeriodPerformanceDto` queda en `Key`/`Label`; `Home.razor` calcula cada tarjeta con la misma serie TWR y el mismo `FilterSeries`/`ComputeSeriesWindowReturn` que el gráfico. Modified Dietz eliminado (y sus tests).

### Backend / infra
- **F2/F3 Docker:** API y Blazor construyen con `docker build` (contexto = carpeta del proyecto). `--no-build` eliminado, COPY del proyecto Blazor eliminado, `.dockerignore` añadido.
- **Compose:** healthcheck `pg_isready` en Postgres + `depends_on: service_healthy` en API; `ASPNETCORE_URLS=http://+:8081` en Blazor.
- **Claves vacías:** `appsettings.json` ya no usa `SET_IN_USER_SECRETS`; sin secretos los proveedores se saltan la llamada (incluido TwelveData, que ya no cae a `demo`).
- **Código muerto fuera:** `GET /performance`, `GetPerformanceAsync`, `ComputePerformanceFromHistoryAsync`, `YahooFinanceService.GetQuoteInfoAsync/GetHistoricalPriceAsync`, campos `Daily/Weekly/...` del DTO.
- **`SymbolClassifier`:** fuente única para fondos (`0P*`, `.EUFUND`) y sufijos; sustituye 5 implementaciones duplicadas.
- **Fallback de símbolo alternativo:** si EOD falla, el composite reintenta con el símbolo primario (antes insistía con el alternativo).
- **Paquetes:** EF Design y Npgsql alineados a 10.0.4; `dotnet-ef` 10.0.10 sigue funcionando. Sin warnings MSB3277.
- **Blazor:** `UseUrls` fuera (launchSettings y `ASPNETCORE_URLS` mandan), `Antiforgery.Key` muerto eliminado, Chart.js servido local y colores desde tokens CSS.

### Frontend
- Diálogo "Añadir a existente": la preselección solo se aplica la primera vez (`_appliedPreselectedId`).
- Traspaso: solo fondos, origen ≠ destino, importe ≤ valor actual.
- Borrado: comprueba el bool del API y avisa si falla.
- Refresco del dashboard serializado con `SemaphoreSlim` (sin carreras).
- Sin fugas de `ex.Message`; errores genéricos + log.
- Modales con `role="dialog"`, `aria-modal`, `aria-labelledby` y cierres con `aria-label`.
- `blazor-error-ui` en español y CSS duplicado eliminado.

### Repo
- README raíz real; eliminados `PortfolioTracker/README.md` (residuo), `src/README.md` y `UnitTest1.cs`.
- Auditoría actualizada con sección "Estado de resolución (11/09/2026)".

### Verificación
- `dotnet build PortfolioTracker.slnx` → OK, 0 errores y sin MSB3277.
- `dotnet test` → **48 OK** (el stub `UnitTest1` eliminado no cuenta).
- `docker build` API y Blazor → OK. `docker compose config` → OK.
- `dotnet tool run dotnet-ef migrations list --no-connect` → OK con Design 10.0.4.

### Pendiente
- Ejecutar `scripts/recalculate_safeback_costbasis.sql` en la BD real una vez (datos previos al fix).
- Commit por work units (F7): todo el trabajo sigue sin commitear.
- Refactor de `Home.razor` y cierre con Escape en modales.
- F4 (auth) y F6 (GET con efectos secundarios) requieren decisión de diseño.

---

# Memory - Sesión 10/09/2026

## Consistencia de rentabilidad y bug SafeBack en el histórico

### Problema
El gráfico en modo **Rentabilidad** mostraba 9,14% en "Inicio" y "1A", mientras la KPI *Ganancia/Pérdida* y la tarjeta *Desde inicio* mostraban 6,65%.

### Diagnóstico (dos causas)
1. **Conceptual:** el gráfico usaba **TWR** (quita el efecto de las aportaciones) y la KPI usaba **rentabilidad simple** (ganancia ÷ invertido). Son métricas distintas y difieren si hay aportaciones dentro del periodo.
2. **Bug:** `HistoryBackfillService` reconstruía las participaciones históricas con `Buy/TransferIn` (+) y `Sell/TransferOut` (−), dejando `SafeBack` en `_ => 0m`. El histórico no incluía las participaciones de SafeBack, pero el snapshot en vivo sí → salto artificial que inflaba el TWR.

### Decisión
La fuente de verdad para *desde inicio* es la **rentabilidad simple** (la de la KPI). TWR se reserva a ventanas acotadas (1S–6M).
SafeBack se mantiene como **rendimiento** (el broker ingresa e invierte por el usuario): suma participaciones, no suma coste y no es flujo externo.

### Cambios backend
- `ReturnCalculators.ComputeSimpleReturnSeries(totals, transactions)`: serie % diaria = (valor − invertido) / invertido. Invertido = compras (importe + comisión) ± traspasos; SafeBack no suma.
- `PortfolioService.GetHistoryAsync`: nuevo `TotalSimpleReturn` en `PortfolioHistoryResponseDto`.
- `HistoryBackfillService.SharesHeldAt`: helper extraído que **cuenta SafeBack como +participaciones**; usado en el replay. Tests añadidos.
- `BackfillAsync(userId, rebuild)`: con `rebuild=true` borra el histórico del usuario antes de regenerarlo. Endpoint `POST /api/portfolio/{userId}/backfill?rebuild=true`.

### Cambios frontend
- `Home.razor`: `IsFullHistoryWindow` detecta cuando la ventana (Inicio/YTD o 1A) cubre todo el histórico. En ese caso el gráfico usa `History.TotalSimpleReturn` y el titular es `SinceInceptionGainLossPercent`; subtítulo "Rentabilidad simple desde inicio (ganancia ÷ invertido)".
- Botón de histórico ahora siempre visible; se convierte en "Recalcular histórico" (rebuild) cuando ya hay datos.
- `PortfolioApiService.BackfillHistoryAsync(userId, rebuild)`.

### Tests
- Nuevos: `ComputeSimpleReturnSeries` (base, aportación, SafeBack) y `HistoryBackfillReplayTests.SharesHeldAt`.
- `dotnet test` → **33 OK**.

### Pendiente
- Ejecutar **"Recalcular histórico"** una vez contra la BD real para reparar los puntos escritos con el replay antiguo (el re-run normal no repara filas existentes).
- Revisar visualmente que Inicio/1A muestren el mismo 6,65%.
- `AGENTS.md` actualizado (SafeBack + Return Metrics chart vs KPI).

---



## Rentabilidad TWR y SafeBack como rendimiento

### Decisiones
- El gráfico principal ahora tiene toggle **Valor / Rentabilidad** y **Rentabilidad es la vista por defecto**.
- La rentabilidad se calcula como **TWR** (Time-Weighted Return), que elimina el efecto de aportaciones y retiradas.
- **SafeBack** se modela como rendimiento: aumenta participaciones pero **no aumenta el precio medio** de la posición.

### Cambios backend
- `TransactionService.AddSafeBackAsync`: ya no suma el importe SafeBack al coste total de la posición.
- Nuevo `ReturnCalculators` con:
  - `ComputeTwrSeries`: rentabilidad acumulada diaria de la cartera.
  - `ComputeItemReturnSeries`: rentabilidad diaria por posición basada en coste acumulado (Buy + TransferIn; SafeBack no suma).
- `PortfolioService.GetHistoryAsync`: devuelve `TotalReturn` e `ItemReturns` además de `Total` e `Items`.
- Script `scripts/recalculate_safeback_costbasis.sql` para recalcular el precio medio de posiciones con SafeBack histórico.

### Cambios frontend
- `PortfolioChart.razor`: soporta modo retorno (`IsReturn`) con formato % y color adaptado.
- `Home.razor`:
  - Toggle Valor/Rentabilidad en el gráfico principal; Rentabilidad por defecto.
  - En modo **Valor** se muestra el cambio de valor en €, no un % (que era engañoso con aportaciones).
  - En modo **Rentabilidad** se muestra el % TWR del periodo.
  - El % del periodo en modo rentabilidad se calcula correctamente desde el TWR acumulado.
  - Corregido `TotalGainLossPercent` para incluir comisiones en el denominador, alineándolo con el % de cada fila.
  - Modal de detalle por posición con toggle Valor/Rentabilidad.

### Tests
- Añadidos `ReturnCalculatorsTests`: TWR sin flujos, con aportación, y SafeBack sin coste.

### Pendiente
- Ejecutar `scripts/recalculate_safeback_costbasis.sql` contra PostgreSQL si hay SafeBacks históricos.
- Revisar visualmente el toggle y las curvas de rentabilidad con datos reales.
- Decidir si en el periodo "Desde inicio" se debe mostrar el mismo % en el gráfico y en la tarjeta de ganancia/pérdida.

---

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
