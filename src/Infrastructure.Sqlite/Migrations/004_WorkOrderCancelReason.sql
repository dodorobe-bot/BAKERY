-- Migration 004: Tambah kolom CancelReason ke tabel WorkOrders
-- Dibutuhkan untuk menyimpan alasan pembatalan SPK yang diinput oleh user

ALTER TABLE WorkOrders ADD COLUMN CancelReason TEXT;
