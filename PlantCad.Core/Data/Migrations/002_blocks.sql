-- Blocks and Plant-Block assignments schema

-- Block definitions table
CREATE TABLE IF NOT EXISTS BlockDef (
    id INTEGER PRIMARY KEY,
    source_path TEXT NOT NULL,
    block_name TEXT NOT NULL,
    block_handle TEXT,
    version_tag TEXT,
    content_hash TEXT NOT NULL,
    unit TEXT,
    width_world REAL,
    height_world REAL,
    created_utc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_utc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    UNIQUE(source_path, block_name)
);

CREATE INDEX IF NOT EXISTS idx_blockdef_name ON BlockDef(block_name);
CREATE INDEX IF NOT EXISTS idx_blockdef_hash ON BlockDef(content_hash);

-- Thumbnails for blocks (PNG bytes)
CREATE TABLE IF NOT EXISTS BlockThumb (
    block_id INTEGER NOT NULL,
    size_px INTEGER NOT NULL,
    png BLOB NOT NULL,
    background TEXT NOT NULL DEFAULT 'transparent',
    updated_utc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    PRIMARY KEY (block_id, size_px),
    FOREIGN KEY(block_id) REFERENCES BlockDef(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_blockthumb_block ON BlockThumb(block_id);

-- Assignments between plants and blocks
CREATE TABLE IF NOT EXISTS PlantBlock (
    plant_id INTEGER NOT NULL,
    block_id INTEGER NOT NULL,
    notes TEXT,
    PRIMARY KEY (plant_id, block_id),
    FOREIGN KEY(plant_id) REFERENCES Plant(id) ON DELETE CASCADE,
    FOREIGN KEY(block_id) REFERENCES BlockDef(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_plantblock_plant ON PlantBlock(plant_id);
CREATE INDEX IF NOT EXISTS idx_plantblock_block ON PlantBlock(block_id);
