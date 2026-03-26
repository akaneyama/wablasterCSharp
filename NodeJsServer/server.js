// server.js
const {
    default: makeWASocket,
    useMultiFileAuthState,
    DisconnectReason,
    fetchLatestBaileysVersion // <-- [BARU] Tambahkan fungsi untuk ambil versi WA terbaru
} = require('@whiskeysockets/baileys');
const { Boom } = require('@hapi/boom');
const express   = require('express');
const qrcode    = require('qrcode');
const fs        = require('fs');
const pino      = require('pino');

const app  = express();
const PORT = 3000;

let sock;                    
let currentQrString = null;  
let connectionStatus = 'disconnected';

app.use(express.json());     

// --------------------------
//  FUNGSI KONEKSI WHATSAPP
// --------------------------
async function connectToWhatsApp () {
    console.log('[⚙️] Memulai koneksi WhatsApp…');

    // [BARU] 1. Ambil versi WhatsApp Web terbaru dari server
    const { version, isLatest } = await fetchLatestBaileysVersion();
    console.log(`[📱] Menggunakan WhatsApp Web v${version.join('.')} (Terbaru: ${isLatest})`);

    // 2. Ambil atau buat kredensial multi‐file
    const { state, saveCreds } = await useMultiFileAuthState('baileys_auth_info');

    // 3. Inisialisasi socket dengan memasukkan 'version'
    sock = makeWASocket({ 
        version, // <-- [BARU] Beritahu Baileys untuk pakai versi terbaru ini
        auth: state,
        logger: pino({ level: 'silent' }) // Sembunyikan log berisik
    });

    // 4. Update kredensial jika berubah (saat scan QR / login berhasil)
    sock.ev.on('creds.update', saveCreds);

    // 5. Handler perubahan koneksi
    sock.ev.on('connection.update', async ({ connection, lastDisconnect, qr }) => {

        // ---- QR diterima ----
        if (qr) {
            currentQrString = qr;
            connectionStatus = 'qr_ready';
            console.log('[📷] QR baru tersedia (akses via http://localhost:3000/qrcode).');
        }

        // ---- Koneksi putus ----
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

                setTimeout(connectToWhatsApp, 2000);      // mulai ulang
            } else if (shouldReconnect) {
                // Beri jeda 2 detik sebelum mencoba reconnect agar tidak spamming ke server WA
                setTimeout(connectToWhatsApp, 2000);      
            }
        }

        // ---- Koneksi berhasil ----
        if (connection === 'open') {
            currentQrString  = null;
            connectionStatus = 'connected';
            console.log('[✅] WhatsApp tersambung dan siap digunakan!');
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
    if (connectionStatus === 'connected') return res.status(400).send('Sudah terhubung, tidak perlu QR.');
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
        let formattedNumber = number.replace(/\D/g, '');
        if (formattedNumber.startsWith('0')) {
            formattedNumber = '62' + formattedNumber.substring(1);
        }
        const jid = formattedNumber + '@s.whatsapp.net';

        const [onWhatsApp] = await sock.onWhatsApp(jid);
        if (!onWhatsApp || !onWhatsApp.exists) {
            return res.status(400).json({ success: false, message: `Nomor ${formattedNumber} tidak terdaftar di WhatsApp.` });
        }

        await sock.sendMessage(jid, { text: message });
        console.log(`[📤] Pesan terkirim ke ${jid}`);
        res.json({ success: true, message: `Pesan terkirim ke ${formattedNumber}` });

    } catch (err) {
        console.error(`❌ Gagal kirim ke ${number}:`, err);
        res.status(500).json({ success: false, message: err.message });
    }
});

// --------------------------
//  HANDLE SIGINT / SIGTERM
// --------------------------
async function gracefulShutdown () {
    console.log('[⛔] Mematikan server & menutup koneksi WhatsApp…');
    try {
        if (sock?.ws) sock.ws.close();
    } catch (err) {
        console.warn('[⚠️] Gagal menutup koneksi:', err.message);
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