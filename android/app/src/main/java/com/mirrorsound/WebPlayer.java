package com.mirrorsound;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.TimeUnit;

/**
 * Lets any browser on the network listen to the phone: "/" is a small player page, "/stream" is an
 * endless HTTP response with the same "MSND" header + 16-bit PCM the PC app receives.
 * Plain Java (no Android APIs), so it can be run and tested on a desktop JVM as well.
 */
final class WebPlayer {
    static final int PORT = 47803;

    private final byte[] page;
    private final byte[] streamHeader;
    private final CopyOnWriteArrayList<Listener> listeners = new CopyOnWriteArrayList<>();
    private final CopyOnWriteArrayList<Socket> sockets = new CopyOnWriteArrayList<>();
    private ServerSocket server;
    private volatile boolean closed;

    WebPlayer(byte[] page, byte[] streamHeader) {
        this.page = page;
        this.streamHeader = streamHeader;
    }

    void start() throws IOException {
        server = new ServerSocket();
        server.setReuseAddress(true);
        server.bind(new InetSocketAddress(PORT));
        Thread accept = new Thread(() -> {
            while (!closed) {
                try {
                    Socket s = server.accept();
                    Thread t = new Thread(() -> handle(s), "mirrorsound-web-client");
                    t.setDaemon(true);
                    t.start();
                } catch (IOException e) {
                    if (closed) return;
                }
            }
        }, "mirrorsound-web");
        accept.setDaemon(true);
        accept.start();
    }

    /** Number of browsers currently playing. */
    int listenerCount() {
        return listeners.size();
    }

    /** Hands a chunk of PCM to every listening browser; never blocks the capture thread. */
    void broadcast(byte[] chunk) {
        for (Listener l : listeners) l.offer(chunk);
    }

    void close() {
        closed = true;
        try {
            if (server != null) server.close();
        } catch (IOException ignored) {
        }
        for (Socket s : sockets) {
            try { s.close(); } catch (IOException ignored) { }
        }
    }

    private void handle(Socket socket) {
        sockets.add(socket);
        try (Socket s = socket) {
            s.setSoTimeout(10000);
            String path = readPath(s.getInputStream());
            if (path == null) return;
            OutputStream out = s.getOutputStream();

            if (path.equals("/stream")) {
                s.setSoTimeout(0);
                s.setTcpNoDelay(true);
                out.write(("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n" +
                        "Cache-Control: no-store\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
                out.write(streamHeader);
                out.flush();
                Listener listener = new Listener();
                listeners.add(listener);
                try {
                    while (!closed) {
                        byte[] chunk = listener.queue.poll(1, TimeUnit.SECONDS);
                        if (chunk != null) out.write(chunk);
                    }
                } finally {
                    listeners.remove(listener);
                }
            } else if (path.equals("/") || path.equals("/index.html")) {
                send(out, "200 OK", "text/html; charset=utf-8", page);
            } else {
                send(out, "404 Not Found", "text/plain", "Not found".getBytes(StandardCharsets.US_ASCII));
            }
        } catch (IOException | InterruptedException ignored) {
            // browser closed the tab or the network dropped
        } finally {
            sockets.remove(socket);
        }
    }

    private static String readPath(InputStream in) throws IOException {
        ByteArrayOutputStream head = new ByteArrayOutputStream();
        int last4 = 0;
        while (last4 != 0x0D0A0D0A) { // read up to the blank line ending the request head
            int b = in.read();
            if (b < 0 || head.size() > 16384) return null;
            head.write(b);
            last4 = (last4 << 8) | b;
        }
        String[] parts = head.toString("US-ASCII").split(" ", 3);
        if (parts.length < 2) return null;
        String path = parts[1];
        int q = path.indexOf('?');
        return q >= 0 ? path.substring(0, q) : path;
    }

    private static void send(OutputStream out, String status, String type, byte[] body) throws IOException {
        out.write(("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.length +
                "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
        out.write(body);
        out.flush();
    }

    /** Per-browser queue; if a browser falls ~0.5 s behind, its backlog is dropped to keep latency low. */
    private static final class Listener {
        final ArrayBlockingQueue<byte[]> queue = new ArrayBlockingQueue<>(50);

        void offer(byte[] chunk) {
            if (!queue.offer(chunk)) {
                queue.clear();
                queue.offer(chunk);
            }
        }
    }
}
