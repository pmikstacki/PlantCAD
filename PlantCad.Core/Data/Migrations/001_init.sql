-- Schema versioning
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_utc TEXT NOT NULL
);

-- Core lookups
CREATE TABLE IF NOT EXISTS PlantType (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Habit (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS FoliagePersistence (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Exposure (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MoistureLevel (
    id INTEGER PRIMARY KEY,
    ordinal INTEGER NOT NULL UNIQUE,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS PhClass (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL,
    min_ph REAL NOT NULL,
    max_ph REAL NOT NULL
);

CREATE TABLE IF NOT EXISTS SoilTrait (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL,
    category TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Color (
    id INTEGER PRIMARY KEY,
    canonical_en TEXT NOT NULL,
    name_pl TEXT NOT NULL,
    hex TEXT
);

CREATE TABLE IF NOT EXISTS Feature (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL,
    group_code TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Packaging (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name_pl TEXT NOT NULL
);

-- Core entity
CREATE TABLE IF NOT EXISTS Plant (
    id INTEGER PRIMARY KEY,
    botanical_genus TEXT NOT NULL,
    botanical_species TEXT,
    cultivar TEXT,
    botanical_name_display TEXT NOT NULL,
    common_name_pl TEXT,
    type_id INTEGER NOT NULL,
    flowering_start_month INTEGER CHECK(flowering_start_month BETWEEN 1 AND 12),
    flowering_end_month INTEGER CHECK(flowering_end_month BETWEEN 1 AND 12),
    hardiness_zone INTEGER,
    hardiness_subzone TEXT,
    height_min_m REAL,
    height_max_m REAL,
    width_min_m REAL,
    width_max_m REAL,
    spacing_min_m REAL,
    spacing_max_m REAL,
    foliage_persistence_id INTEGER,
    ph_min REAL,
    ph_max REAL,
    ph_class_min_id INTEGER,
    ph_class_max_id INTEGER,
    moisture_min_level INTEGER,
    moisture_max_level INTEGER,
    habit_primary_id INTEGER,
    raw_height TEXT,
    raw_width TEXT,
    raw_spacing TEXT,
    raw_ph TEXT,
    raw_moisture TEXT,
    raw_exposure TEXT,
    raw_soil TEXT,
    raw_flower_props TEXT,
    raw_leaf_props TEXT,
    raw_fruit_color TEXT,
    raw_stock TEXT,
    created_utc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_utc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    FOREIGN KEY(type_id) REFERENCES PlantType(id),
    FOREIGN KEY(foliage_persistence_id) REFERENCES FoliagePersistence(id),
    FOREIGN KEY(ph_class_min_id) REFERENCES PhClass(id),
    FOREIGN KEY(ph_class_max_id) REFERENCES PhClass(id),
    FOREIGN KEY(moisture_min_level) REFERENCES MoistureLevel(id),
    FOREIGN KEY(moisture_max_level) REFERENCES MoistureLevel(id),
    FOREIGN KEY(habit_primary_id) REFERENCES Habit(id)
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_plant_taxon ON Plant(botanical_genus, botanical_species, IFNULL(cultivar,''));
CREATE INDEX IF NOT EXISTS idx_plant_type ON Plant(type_id);
CREATE INDEX IF NOT EXISTS idx_plant_zone ON Plant(hardiness_zone, hardiness_subzone);
CREATE INDEX IF NOT EXISTS idx_plant_dims ON Plant(height_min_m, height_max_m, width_min_m, width_max_m);

-- Associations
CREATE TABLE IF NOT EXISTS PlantExposure (
    plant_id INTEGER NOT NULL,
    exposure_id INTEGER NOT NULL,
    PRIMARY KEY (plant_id, exposure_id),
    FOREIGN KEY(plant_id) REFERENCES Plant(id) ON DELETE CASCADE,
    FOREIGN KEY(exposure_id) REFERENCES Exposure(id)
);

CREATE TABLE IF NOT EXISTS PlantHabit (
    plant_id INTEGER NOT NULL,
    habit_id INTEGER NOT NULL,
    is_primary INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (plant_id, habit_id),
    FOREIGN KEY(plant_id) REFERENCES Plant(id) ON DELETE CASCADE,
    FOREIGN KEY(habit_id) REFERENCES Habit(id)
);

CREATE TABLE IF NOT EXISTS PlantSoilTrait (
    plant_id INTEGER NOT NULL,
    soil_trait_id INTEGER NOT NULL,
    PRIMARY KEY (plant_id, soil_trait_id),
    FOREIGN KEY(plant_id) REFERENCES Plant(id) ON DELETE CASCADE,
    FOREIGN KEY(soil_trait_id) REFERENCES SoilTrait(id)
);

CREATE TABLE IF NOT EXISTS PlantColor (
    plant_id INTEGER NOT NULL,
    attribute TEXT NOT NULL CHECK(attribute IN ('autumn_foliage','flower','fruit')),
    color_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (plant_id, attribute, color_id),
    FOREIGN KEY(plant_id) REFERENCES Plant(id) ON DELETE CASCADE,
    FOREIGN KEY(color_id) REFERENCES Color(id)
);

CREATE TABLE IF NOT EXISTS PlantFeature (
    plant_id INTEGER NOT NULL,
    feature_id INTEGER NOT NULL,
    PRIMARY KEY (plant_id, feature_id),
    FOREIGN KEY(plant_id) REFERENCES Plant(id) ON DELETE CASCADE,
    FOREIGN KEY(feature_id) REFERENCES Feature(id)
);

CREATE TABLE IF NOT EXISTS PlantVariant (
    id INTEGER PRIMARY KEY,
    plant_id INTEGER NOT NULL,
    packaging_id INTEGER,
    container_code TEXT,
    height_min_cm INTEGER,
    height_max_cm INTEGER,
    circumference_min_cm INTEGER,
    circumference_max_cm INTEGER,
    notes TEXT,
    raw_variant TEXT,
    FOREIGN KEY(plant_id) REFERENCES Plant(id) ON DELETE CASCADE,
    FOREIGN KEY(packaging_id) REFERENCES Packaging(id)
);

-- Seeds
INSERT OR IGNORE INTO Exposure(code, name_pl) VALUES
  ('full_sun','Słoneczne'),
  ('partial_shade','Półcień'),
  ('shade','Cień');

INSERT OR IGNORE INTO MoistureLevel(ordinal, code, name_pl) VALUES
  (1, 'dry', 'Sucha'),
  (2, 'moderate', 'Umiarkowana'),
  (3, 'moist', 'Wilgotna'),
  (4, 'wet', 'Mokra');

INSERT OR IGNORE INTO PhClass(code, name_pl, min_ph, max_ph) VALUES
  ('acidic','Kwaśny',5.0,6.5),
  ('neutral','Obojętny',6.5,7.5),
  ('alkaline','Zasadowy',7.5,8.5);

INSERT OR IGNORE INTO PlantType(code, name_pl) VALUES
  ('tree','Drzewo');

INSERT OR IGNORE INTO Habit(code, name_pl) VALUES
  ('columnar','Kolumnowy'),
  ('narrow_columnar','Wąskokolumnowy'),
  ('weeping','Płaczący'),
  ('conical','Stożkowaty'),
  ('broad','Szeroki, rozłożysty'),
  ('oval','Owalny'),
  ('umbrella','Parasolowaty'),
  ('irregular','Nieregularny'),
  ('loose','Luźny'),
  ('multi_stem','Wielopniowy');

INSERT OR IGNORE INTO FoliagePersistence(code, name_pl) VALUES
  ('deciduous','Sezonowe'),
  ('evergreen','Zimozielone'),
  ('semi_evergreen','Półzimozielone');
