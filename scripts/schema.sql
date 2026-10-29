CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025230723_InitialCreate') THEN
    CREATE TABLE customers (
        "Id" uuid NOT NULL,
        "Name" character varying(150) NOT NULL,
        "SubjectId" character varying(200) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_customers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025230723_InitialCreate') THEN
    CREATE TABLE accounts (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "Balance" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_accounts" PRIMARY KEY ("Id"),
        CONSTRAINT ck_balance CHECK ("Balance" >= 0),
        CONSTRAINT ck_currency CHECK ("Currency" = 'BRL'),
        CONSTRAINT "FK_accounts_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES customers ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025230723_InitialCreate') THEN
    CREATE TABLE movements (
        "Id" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "Type" character varying(6) NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "RegisteredAt" timestamp with time zone NOT NULL,
        "IdempotencyKey" character varying(100) NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        CONSTRAINT "PK_movements" PRIMARY KEY ("Id"),
        CONSTRAINT ck_amount CHECK ("Amount" > 0),
        CONSTRAINT ck_type CHECK ("Type" IN ('Credit', 'Debit')),
        CONSTRAINT "FK_movements_accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES accounts ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025230723_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_accounts_CustomerId" ON accounts ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025230723_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_customers_SubjectId" ON customers ("SubjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025230723_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_movements_AccountId_IdempotencyKey" ON movements ("AccountId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025230723_InitialCreate') THEN
    CREATE INDEX "IX_movements_AccountId_RegisteredAt_Id" ON movements ("AccountId", "RegisteredAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025230723_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261025230723_InitialCreate', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028000100_ProtectLedger') THEN
    CREATE FUNCTION reject_movement_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
        RAISE EXCEPTION 'Confirmed financial movements are immutable' USING ERRCODE = '23514';
    END;
    $$;
    CREATE TRIGGER movements_immutable BEFORE UPDATE OR DELETE ON movements
    FOR EACH ROW EXECUTE FUNCTION reject_movement_mutation();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028000100_ProtectLedger') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261028000100_ProtectLedger', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029225204_AddTransactionalMessaging') THEN
    CREATE TABLE outbox_messages (
        "Id" uuid NOT NULL,
        "Payload" jsonb NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "PublishedAt" timestamp with time zone,
        CONSTRAINT "PK_outbox_messages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029225204_AddTransactionalMessaging') THEN
    CREATE TABLE processing_receipts (
        "EventId" uuid NOT NULL,
        "MovementId" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "ProcessedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_processing_receipts" PRIMARY KEY ("EventId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029225204_AddTransactionalMessaging') THEN
    CREATE INDEX "IX_outbox_messages_CreatedAt_Id" ON outbox_messages ("CreatedAt", "Id") WHERE "PublishedAt" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029225204_AddTransactionalMessaging') THEN
    CREATE INDEX "IX_processing_receipts_AccountId" ON processing_receipts ("AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029225204_AddTransactionalMessaging') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261029225204_AddTransactionalMessaging', '10.0.14');
    END IF;
END $EF$;
COMMIT;

