-- Recalcula el precio medio de compra de posiciones con SafeBack histórico
-- tras el cambio de modelo: SafeBack aporta participaciones pero NO coste.
-- Solo las transacciones de tipo Buy y TransferIn contribuyen al coste.
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
      AND t."Type" IN ('Buy', 'TransferIn')
)
WHERE i."SafeBackAmount" > 0
  AND i."Shares" > 0;

COMMIT;
