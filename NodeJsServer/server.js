// server.js
const {
    default: makeWASocket,
    useMultiFileAuthState,
    DisconnectReason,
} = require('@whiskeysockets/baileys');
const { Boom } = require('@hapi/boom');
const express   = require('express');
const qrcode    = require('qrcode');
const fs        = require('fs');

const app  = express();
const PORT = 3000;

let sock;                    // instance socket
let currentQrString = null;  // QR terkini (jika ada)
let connectionStatus = 'disconnected';

app.use(express.json());     // agar bisa parsing body JSON

// --------------------------
//  FUNGSI KONEKSI WHATSAPP
// --------------------------
async function connectToWhatsApp () {
    console.log('[⚙️] Memulai koneksi WhatsApp…');

    // ambil atau buat kredensial multi‐file
    const { state, saveCreds } = await useMultiFileAuthState('baileys_auth_info');

    // inisialisasi socket
    sock = makeWASocket({ auth: state /* tanp a printQRInTerminal */ });

    // update kredensial jika berubah
    sock.ev.on('creds.update', saveCreds);

    // handler perubahan koneksi
    sock.ev.on('connection.update', async ({ connection, lastDisconnect, qr }) => {

        // ---- QR diterima ----
        if (qr) {
            currentQrString = qr;
            connectionStatus = 'qr_ready';
            console.log('[📷] QR baru tersedia (akses via /qrcode).');
        }

        // ---- koneksi putus ----
        if (connection === 'close') {
            currentQrString  = null;
            connectionStatus = 'disconnected';

            const statusCode = (lastDisconnect?.error instanceof Boom)
                ? lastDisconnect.error.output.statusCode
                : null;

            const loggedOut = statusCode === DisconnectReason.loggedOut;
            const shouldReconnect = !loggedOut;

            console.log('[🔌] Koneksi terputus.', { statusCode, reconnect: shouldReconnect });

            if (loggedOut) {
                // hapus folder auth → paksa scan ulang
                console.log('[🔐] Session kedaluwarsa, hapus kredensial & siapkan QR baru…');
                const authDir = 'baileys_auth_info';
                if (fs.existsSync(authDir)) fs.rmSync(authDir, { recursive: true, force: true });

                setTimeout(connectToWhatsApp, 1000);      // mulai ulang
            } else if (shouldReconnect) {
                setTimeout(connectToWhatsApp, 1000);      // coba reconnect
            }
        }

        // ---- koneksi berhasil ----
        if (connection === 'open') {
            currentQrString  = null;
            connectionStatus = 'connected';
            console.log('[✅] WhatsApp tersambung.');
        }
    });
}

// --------------------------
//  ROUTE EXPRESS
// --------------------------
app.get('/status', (_req, res) => {
    res.json({ status: connectionStatus });
});

app.get('/qrcode', async (_req, res) => {
    if (!currentQrString) return res.status(404).send('QR belum tersedia.');

    try {
        const img = await qrcode.toBuffer(currentQrString, { type: 'png' });
        res.setHeader('Content-Type', 'image/png');
        res.send(img);
    } catch (err) {
        console.error('❌ Gagal membuat QR buffer:', err);
        res.status(500).send('Gagal membuat QR.');
    }
});

app.post('/send', async (req, res) => {
    if (connectionStatus !== 'connected' || !sock)
        return res.status(400).json({ success: false, message: 'WhatsApp belum terhubung.' });

    const { number, message } = req.body;
    if (!number || !message)
        return res.status(400).json({ success: false, message: 'Nomor & pesan wajib diisi.' });

    try {
        const jid = number.replace(/\D/g, '') + '@s.whatsapp.net';
        await sock.sendMessage(jid, { text: message });
        console.log(`[📤] Pesan terkirim ke ${jid}`);
        res.json({ success: true, message: `Pesan terkirim ke ${number}` });
    } catch (err) {
        console.error(`❌ Gagal kirim ke ${number}:`, err);
        res.status(500).json({ success: false, message: err.message });
    }
});

// --------------------------
//  HANDLE SIGINT / SIGTERM
// --------------------------
async function gracefulShutdown () {
    console.log('[⛔] Mematikan server & koneksi WhatsApp…');
    try {
        if (sock?.ws?.readyState === 1 && sock.logout) await sock.logout();
    } catch (err) {
        console.warn('[⚠️] Logout gagal (mungkin koneksi sudah tutup):', err.message);
    }
    process.exit(0);
}
process.on('SIGINT',  gracefulShutdown);
process.on('SIGTERM', gracefulShutdown);

// --------------------------
//  START SERVER
// --------------------------
app.listen(PORT, () => {
    console.log(`🚀 Server API aktif di http://localhost:${PORT}`);
    connectToWhatsApp().catch(err => console.error('❌ Gagal koneksi WhatsApp:', err));
});
