-- Recalcula el precio medio de compra de posiciones con SafeBack histórico.
-- Modelo actual: SafeBack es una aportación externa (como si el usuario ingresara
-- el dinero), así que suma participaciones Y coste. El coste total es la suma de
-- Buy + TransferIn + SafeBack dividida entre las participaciones actuales.
-- Script idempotente: puede re-ejecutarse tras restaurar un backup o cambiar el modelo.
--
-- Uso:
--   psql -U <usuario> -d <base_de_datos> -f scripts/recalculate_safeback_costbasis.sql
--
-- Recomendación: hacer una copia de seguridad de la base de datos antes de ejecutar.

BEGIN;

UPDATE "PortfolioItems" i
SET "PurchasePrice" = (
    SELECT COALESCE(SUM(t."AmountEur"), 0) / NULLIF(i."Shares", 0)
    FROM "PortfolioTransactions" t
    WHERE t."ItemId" = i."Id"
      AND t."Type" IN ('Buy', 'TransferIn', 'SafeBack')
)
WHERE i."SafeBackAmount" > 0
  AND i."Shares" > 0;

COMMIT;
