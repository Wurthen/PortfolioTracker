# Plan — Multi-portfolio + importación de extractos (CSV/PDF)

Fecha: 12/09/2026
Estado: acordado con el usuario, pendiente de ejecución
Decisiones cerradas el 11/09. Alcance v1 del import CSV: **trades + avisos**.

## Objetivo

1. Soportar **varios portfolios por usuario** (varios brokers/estrategias) con selector en la UI.
2. **Importar CSV de Trade Republic** (export de transacciones) como ledger de operaciones.
3. **Importar PDF de confirmaciones de compra de otro broker** (formato aún por identificar; requiere muestras).

## Contexto de partida

- Los placeholders ya existen desde el 07/09: `PortfolioSelector.razor`, `ImportButton.razor` e `ImportDialog.razor` ("Próximamente").
- `mcp-pandas` ya está integrado en el repo (`mcp-pandas/`), corre en Docker (`docker-compose.yml`, puerto 8082) y está configurado en opencode como MCP remoto. Perfila CSV/Excel, ejecuta pandas en sandbox y genera gráficos Chart.js.
- No existe entidad `Portfolio`: `PortfolioItems`, `PortfolioTransactions` y `PortfolioHistoryPoints` están particionados solo por `UserId`. `SymbolPrices` es caché global y **se mantiene global**.
- CSV real de Trade Republic disponible en `csv/Exportación de transacción.csv` (gitignored): 726 filas, 197 TRADING (116 BUY / 81 SELL), 46 ISINs, 7 fondos, 4 cripto, 12 filas `BENEFITS_SAVEBACK`, acciones corporativas de Amper y transferencias de efectivo.
- `pdf/` está vacío (gitignored) y `data/` **no** está en `.gitignore` (añadirlo en Fase 0).
- El árbol de trabajo tenía los cambios de SafeBack sin commitear; se commitean como work unit previo al refactor.

## Decisiones

- **Import CSV v1**: se importan BUY/SELL (items + transacciones, coste ponderado, comisiones desde `fee`, ISIN → símbolo). Solo se crean items con participaciones netas > 0 al final del extracto.
  - `BENEFITS_SAVEBACK`, acciones corporativas (Amper) y transferencias de efectivo → **solo avisos en el preview**, no tocan la BD.
- **Idempotencia**: el `transaction_id` de Trade Republic se persiste como `ExternalId`; reimportar el mismo fichero no duplica.
- **Import PDF**: serán confirmaciones de compra de un broker distinto a Trade Republic; el parser no asumirá formato TR. Bloqueado hasta tener 2-3 muestras en `pdf/`.
- **Idioma**: artefactos de código/docs en inglés; este plan y las notas, en español (convención de `notes/`).

## Fase 0 — Herramientas y limpieza previa

- Instalar MCP de PDF en `C:\Users\Usuario\.config\opencode\opencode.jsonc`:
  `"pdf-reader": { "type": "local", "command": ["uvx", "mcp-pdf-reader"], "enabled": true }` (PyMuPDF, soporte OCR). `uv`/`uvx` ya están instalados. Alternativa npm: `@rturv/mcp-pdf-reader`.
- Añadir `data/` a `.gitignore` (artefactos de mcp-pandas; hoy no está cubierto).
- Commit previo del trabajo de SafeBack (work unit ya terminado; tests en verde).

## Fase 1 — Multi-portfolio backend

### Modelo y migración

- Nueva entidad `Portfolio`: `Id`, `UserId`, `Name` (max 100), `CreatedAt`, `UpdatedAt`.
- `PortfolioId` en `PortfolioItem`, `PortfolioTransaction` y `PortfolioHistoryPoint`; se elimina `UserId` de las tres (el propietario vive en `Portfolio`). `SymbolPrice` sin cambios.
- `ExternalId` (string?, max 100) en `PortfolioTransaction` para idempotencia de imports.
- Índices nuevos: items único `(PortfolioId, Symbol)`; histórico único `(PortfolioId, ItemId, Date)`; transacciones `(PortfolioId, ItemId)`; import único filtrado por `(PortfolioId, ExternalId)`.
- Migración EF `AddPortfolios` con SQL de backfill: un portfolio "Mi portfolio" por usuario existente (distinct sobre items ∪ transacciones ∪ histórico), reasignar hijos por `UserId`, luego drop de columnas viejas y creación de índices.
- **Backup de la BD local antes de migrar** (`pg_dump` a `%TEMP%\opencode`). La API migra automáticamente al arrancar.

### API

- Nuevas rutas:
  - `GET /api/users/{userId:guid}/portfolios` — lista.
  - `POST /api/users/{userId:guid}/portfolios` — crear `{ name }`.
  - `PUT /api/portfolios/{id:guid}/{userId:guid}` — renombrar.
  - `DELETE /api/portfolios/{id:guid}/{userId:guid}` — borrar (guardas: no borrar el último; cascada de items/transacciones/histórico).
- Mover los endpoints actuales a `/api/portfolios/{portfolioId:guid}/...`:
  `GET` items, `POST/PUT/DELETE` items, `dashboard`, `history`, `backfill`, `transactions` (listar/crear, por item), `safeback`, `transfers`, `performance-detailed`.
- `CreatePortfolioItemRequest`: fuera `UserId` (va en la ruta).

### Servicios

- Renombrar `userId → portfolioId` en `PortfolioService`, `TransactionService` y `HistoryBackfillService` (firmas y queries). `EnsureSeededAsync` pasa a ser por portfolio.
- `ReturnCalculators` y `PerformanceCalculators` no cambian (trabajan por item/serie).

## Fase 2 — Multi-portfolio frontend

- `PortfolioStateService` (scoped por circuito Blazor Server): `Portfolios`, `ActivePortfolioId`, `ActivePortfolioName`, evento de cambio e inicialización (crea el primer portfolio si no hay ninguno).
- `PortfolioSelector` real: lista de portfolios, activo marcado, acciones "Nuevo portfolio", renombrar y borrar (diálogo propio).
- `Home.razor` y diálogos (`AddItemDialog`, `EditItemDialog`, `SafeBackDialog`, `TransferDialog`): usar `ActivePortfolioId` en lugar de `UserId`; recargar dashboard/histórico al cambiar de portfolio.
- Mantener `UserIdService` (usuario fijo de dev) solo para la gestión de portfolios.

## Fase 3 — Import CSV (Trade Republic)

- Dependencia backend: `CsvHelper` (MIT). El CSV trae comas dentro de `description` entre comillas; hace falta un parser RFC 4180 real.
- Nuevos componentes en la API:
  - `TradeRepublicCsvParser`: filas → `ParsedStatementRow` (fecha, categoría, tipo, asset class, nombre, ISIN, participaciones con signo normalizado, precio, importe, fee, tax, moneda, descripción, transaction_id).
  - `IsinSymbolResolver` + tabla `InstrumentMappings` (`Isin` PK, `Symbol`, `Name`, `Type`, `Source`, `UpdatedAt`): acciones/ETF vía búsqueda Yahoo por ISIN; fondos `{ISIN}.EUFUND`; cripto `BTC-EUR`/`ETH-EUR`. Persistida para previews rápidos y futuras ediciones manuales.
  - `ImportPlanner` puro: replay por fecha, coste ponderado, validación ventas ≤ participaciones, detección de duplicados por `ExternalId`, generación de avisos.
  - `CsvImportService`: orquesta parse → resolver → plan; `ApplyAsync` en transacción de BD.
- Endpoints:
  - `POST /api/portfolios/{id}/import/csv/preview` (multipart) → resumen + avisos, **sin escrituras**.
  - `POST /api/portfolios/{id}/import/csv/apply` (multipart) → aplica a un portfolio existente (la UI crea el portfolio nuevo antes, si aplica).
- UI: convertir `ImportDialog` en wizard real (selección de fichero → subida a preview → tabla resumen y avisos → confirmar → recargar dashboard). Validar extensión y tamaño (límite ~10 MB).
- Tests: parser (comas entrecomilladas, signos BUY/SELL, `CultureInfo.InvariantCulture`), planner (ponderado, venta sobre participaciones, duplicados), resumen de preview.

## Fase 4 — Import PDF

- Dependencia backend: `PdfPig` (Apache 2.0) para extracción de texto.
- Parser del broker a identificar con las muestras (`pdf/` gitignored): extracción por plantilla de las confirmaciones de compra (ISIN, nombre, cantidad, precio, comisión, fecha, operación).
- Reutiliza el mismo pipeline `ImportPlanner` + endpoints `preview`/`apply` (multipart PDF).
- Escaneados/OCR y brokers múltiples: fuera de alcance v1 (mensaje de error claro).
- **Bloqueante**: 2-3 PDFs reales en `pdf/`.

## Fase 5 — Cierre

- `dotnet build PortfolioTracker.slnx` + `dotnet test tests/PortfolioTracker.Tests` en verde.
- `docker build` de API y Blazor; prueba E2E manual con el CSV real (con claves vacías los proveedores no hacen llamadas; se usará `SymbolPrices`).
- Actualizar `AGENTS.md` (rutas nuevas, feature de import, tabla `InstrumentMappings`) y `notes/memory.md`.
- Commits por work unit.

## Riesgos

- **Migración sobre datos reales**: backup previo obligatorio; revisar el SQL de backfill contra una copia.
- **Cuota de proveedores**: importar 46 símbolos dispara precios en el primer dashboard; EOD free tier = 20 llamadas/día.
- **`Home.razor` (999 líneas)**: todo el flujo de import vive en su propio componente; no engordar la página.
- **Precisión del replay**: SELL antes de BUY o posiciones cerradas generan avisos; no se crean items con 0 participaciones.
- **PDF desconocido**: sin muestras no se estima esfuerzo; puede requerir reglas por plantilla.

## Orden de ejecución

1. Fase 0 (MCP PDF, `.gitignore`, commit SafeBack).
2. Fase 1 (modelo + migración con backup + API + servicios).
3. Fase 2 (estado + selector + páginas).
4. Fase 3 (parser + planner + endpoints + wizard + tests).
5. Fase 4 cuando haya muestras en `pdf/`.
6. Fase 5 (verificación, docs y commits).
