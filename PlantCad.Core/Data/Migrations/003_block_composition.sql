-- 003: add BlockComposition table to store serialized block components
CREATE TABLE IF NOT EXISTS BlockComposition (
  block_id INTEGER PRIMARY KEY REFERENCES BlockDef(id) ON DELETE CASCADE,
  composition TEXT NOT NULL,
  updated_utc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP)
);

-- Helpful index (primary key already covers lookups by block_id)
