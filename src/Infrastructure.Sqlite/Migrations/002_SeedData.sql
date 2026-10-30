-- Seruni 22 Bakery - Seed Data v002

-- Default Chart of Accounts
INSERT OR IGNORE INTO Accounts (Id, Code, Name, Type, IsSystem, IsActive) VALUES
(1,  '1-001', 'Kas & Bank',              'Asset',     1, 1),
(2,  '1-002', 'Piutang Usaha',           'Asset',     1, 1),
(3,  '2-001', 'Uang Muka Pelanggan',     'Liability', 1, 1),
(4,  '3-001', 'Modal',                   'Equity',    1, 1),
(5,  '4-001', 'Pendapatan Penjualan',    'Revenue',   1, 1),
(6,  '5-001', 'Beban Bahan Baku (HPP)', 'Expense',   1, 1),
(7,  '5-002', 'Beban Operasional',       'Expense',   1, 1),
(8,  '5-003', 'Beban Gaji',              'Expense',   1, 1);

-- Default admin user (password: admin123 - MUST CHANGE on first login)
-- Hash generated with BCrypt workFactor=12
INSERT OR IGNORE INTO Users (Id, Username, PasswordHash, Role, IsActive, MustChangePassword)
VALUES (1, 'admin',
        '$2a$12$/zeyZdxVvLbYbcYY3XMa/u21xrbme45McKHAB0YH1ayfKhL9FkcAG',
        'Owner', 1, 1);

-- Default app settings
INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES
('brand_name',    'Seruni 22 Bakery'),
('brand_tagline', 'Sistem Manajemen Toko Kue'),
('timezone',      'Asia/Jakarta'),
('currency',      'IDR'),
('app_version',   '1.0.0');

-- Schema version record
INSERT OR IGNORE INTO SchemaVersion (Version, AppliedAt) VALUES (2, datetime('now'));
