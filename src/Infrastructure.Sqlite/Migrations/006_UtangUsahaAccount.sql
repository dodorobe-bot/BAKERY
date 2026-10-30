-- Tambahkan akun Utang Usaha (Liability) ke Chart of Accounts
-- Diperlukan untuk jurnal penerimaan barang: Dr Persediaan Bahan Baku / Cr Utang Usaha
INSERT OR IGNORE INTO Accounts (Id, Code, Name, Type, IsSystem, IsActive)
VALUES (14, '2-002', 'Utang Usaha', 'Liability', 1, 1);
