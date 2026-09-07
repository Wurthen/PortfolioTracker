-- Script para reconstruir la inversión de oro con aportaciones + SafeBack.
-- Reemplaza los tres valores de abajo antes de ejecutar.
-- El GUID del item es fijo para que las transacciones lo referencien.

-- UserId fijo de la app: 12345678-1234-1234-1234-123456789012
-- symbol: PHYMF
-- name:   Invesco Physical Gold ETC

BEGIN;

-- Limpia item anterior del mismo símbolo para ese usuario
DELETE FROM "PortfolioHistoryPoints"
WHERE "UserId" = '12345678-1234-1234-1234-123456789012'::uuid
  AND "ItemId" IN (
      SELECT "Id" FROM "PortfolioItems"
      WHERE "UserId" = '12345678-1234-1234-1234-123456789012'::uuid AND "Symbol" = 'PHYMF'
  );

DELETE FROM "PortfolioTransactions"
WHERE "UserId" = '12345678-1234-1234-1234-123456789012'::uuid
  AND "ItemId" IN (
      SELECT "Id" FROM "PortfolioItems"
      WHERE "UserId" = '12345678-1234-1234-1234-123456789012'::uuid AND "Symbol" = 'PHYMF'
  );

DELETE FROM "PortfolioItems"
WHERE "UserId" = '12345678-1234-1234-1234-123456789012'::uuid AND "Symbol" = 'PHYMF';

-- Inserta el item reconstruido
INSERT INTO "PortfolioItems" (
    "Id", "UserId", "Symbol", "Name", "Type",
    "Shares", "PurchasePrice", "PurchaseDate", "Commission",
    "AlternativeSymbol", "UseAlternativeSymbol",
    "SafeBackAmount", "SafeBackShares", "CreatedAt", "UpdatedAt"
) VALUES (
    '11111111-1111-1111-1111-111111111111'::uuid,
    '12345678-1234-1234-1234-123456789012'::uuid,
    'PHYMF',
    'Invesco Physical Gold ETC',
    'ETF',
    3.91342593,
    71.9446,
    '2026-09-02',
    0,
    NULL,
    false,
    31.55,
    0.447448,
    NOW(),
    NOW()
);

-- Inserta las transacciones (aportaciones propias + SafeBack)
INSERT INTO "PortfolioTransactions" (
    "Id", "UserId", "ItemId", "Type", "Date", "Shares", "AmountEur", "Commission", "LinkedTransactionId", "CreatedAt"
) VALUES
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'Buy',      '2026-05-04', 0.6595436,  50.00, 0, NULL, NOW()),
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'Buy',      '2026-06-02', 0.66269052, 50.00, 0, NULL, NOW()),
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'SafeBack', '2026-06-02', 0.0753016,   5.68, 0, NULL, NOW()),
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'Buy',      '2026-07-02', 0.72170901, 50.00, 0, NULL, NOW()),
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'SafeBack', '2026-07-02', 0.11518476,  7.98, 0, NULL, NOW()),
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'Buy',      '2026-08-03', 0.73190368, 50.00, 0, NULL, NOW()),
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'SafeBack', '2026-08-03', 0.17503293, 11.96, 0, NULL, NOW()),
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'SafeBack', '2026-09-02', 0.08192871,  5.93, 0, NULL, NOW()),
    (gen_random_uuid(), '12345678-1234-1234-1234-123456789012'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'Buy',      '2026-09-02', 0.69013112, 50.00, 0, NULL, NOW());

COMMIT;
