#!/usr/bin/env python3
"""UDP 体感设备模拟器(M2 联调工具,仅标准库)。

按原作 UdpServer 协议向 8281/udp 发小端二进制包:
  [0]      'R'(戒指/右手)或 'L'(腿部/左手)
  [1..16]  4 x float32 四元数 x,y,z,w(游戏端归一化)
  [17..20] int32 压力值(原作未用,固定发 512)
  [21]     扳机 k(0/1)
  [22]     换枪 k2(0/1)
(原作 C# 连续读 23 字节;方案表"22 字节"为 1 基偏移写法。)

同时在 8282/udp 监听,打印游戏端发来的 Heart / Fortune / Reset。

用法:
  python udp_device_sim.py                  # 持续发 'R' 摆动包(手动联调 udp_test 场景)
  python udp_device_sim.py --selftest       # 自检编排时序(R+L 双路):
                                            #   0-5s  固定标记四元数
                                            #   5-10s 绕 Y/X 摆动 + 周期扳机/换枪
                                            #   10-16s 静默(验 2s 断线 / 5s socket 重建)
                                            #   16-20s 标记四元数(验重连)
  python udp_device_sim.py --side L --ip 192.168.1.10 --rate 60
"""

import argparse
import math
import socket
import struct
import threading
import time

LISTEN_PORT = 8282
PRESSURE = 512
# 自检标记四元数(未归一化;游戏端归一化后按分量比对,dot>0.999)
MARKER = (0.1, -0.2, 0.3, 0.9)


def quat_axis_angle(ax, ay, az, deg):
    half = math.radians(deg) / 2.0
    s = math.sin(half)
    return (ax * s, ay * s, az * s, math.cos(half))


def quat_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def build_packet(flag: bytes, q, pressure: int, k: int, k2: int) -> bytes:
    return flag + struct.pack("<4fiBB", q[0], q[1], q[2], q[3], pressure, k, k2)


def recv_loop(stop: threading.Event) -> None:
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    s.bind(("", LISTEN_PORT))
    s.settimeout(0.5)
    while not stop.is_set():
        try:
            data, addr = s.recvfrom(1024)
        except socket.timeout:
            continue
        except OSError:
            break
        print(f"[sim-recv t+{time.monotonic() - recv_loop.t0:6.2f}s] "
              f"{addr[0]}:{addr[1]} -> {data.decode('utf-8', 'replace')}", flush=True)
    s.close()


def swing_quat(t: float):
    """绕 Y(±25°, 0.5Hz)与 X(±15°, 0.33Hz)摆动。"""
    yaw = 25.0 * math.sin(2.0 * math.pi * 0.5 * t)
    pitch = 15.0 * math.sin(2.0 * math.pi * 0.33 * t)
    return quat_mul(quat_axis_angle(0, 1, 0, yaw), quat_axis_angle(1, 0, 0, pitch))


def key_pattern(t: float):
    """2 秒周期:[0,0.5) 扳机;[1.0,1.5) 换枪。"""
    ph = t % 2.0
    return (1 if ph < 0.5 else 0), (1 if 1.0 <= ph < 1.5 else 0)


def send_loop(args, flags, duration: float, packet_at) -> None:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    t0 = time.monotonic()
    recv_loop.t0 = t0
    stop = threading.Event()
    th = threading.Thread(target=recv_loop, args=(stop,), daemon=True)
    th.start()
    interval = 1.0 / args.rate
    last_log = -1
    quiet_logged = False
    while True:
        t = time.monotonic() - t0
        if t >= duration:
            break
        pkt = packet_at(t)
        if pkt is None:
            if not quiet_logged:
                print(f"[sim-send t={t:6.2f}] 静默中(不发包)", flush=True)
                quiet_logged = True
        else:
            quiet_logged = False
            q, k, k2 = pkt
            for flag in flags:
                sock.sendto(build_packet(flag, q, PRESSURE, k, k2), (args.ip, args.port))
            half = int(t * 2)
            if half != last_log:
                last_log = half
                print(f"[sim-send t={t:6.2f}] flags={b''.join(flags).decode()} "
                      f"q=({q[0]:+.4f},{q[1]:+.4f},{q[2]:+.4f},{q[3]:+.4f}) k={k} k2={k2}",
                      flush=True)
        time.sleep(interval)
    stop.set()
    th.join(timeout=1.0)
    sock.close()


def run_selftest(args) -> None:
    def packet_at(t):
        if t < 5.0 or 16.0 <= t < 20.0:
            return MARKER, 0, 0
        if t < 10.0:
            k, k2 = key_pattern(t)
            return swing_quat(t), k, k2
        if t < 16.0:
            return None  # 静默:验 2 秒断线 + 5 秒 socket 重建
        return None
    print("[sim] 自检时序: 0-5s 标记 | 5-10s 摆动+按键 | 10-16s 静默 | 16-20s 标记", flush=True)
    send_loop(args, (b"R", b"L"), 20.5, packet_at)


def run_continuous(args) -> None:
    def packet_at(t):
        k, k2 = key_pattern(t)
        return swing_quat(t), k, k2
    print(f"[sim] 持续发送 '{args.side}' 摆动包 → {args.ip}:{args.port},Ctrl+C 停止", flush=True)
    try:
        send_loop(args, (args.side.encode(),), float("inf"), packet_at)
    except KeyboardInterrupt:
        pass


def main() -> None:
    ap = argparse.ArgumentParser(description="UDP 体感设备模拟器(M2)")
    ap.add_argument("--ip", default="127.0.0.1", help="游戏所在主机 IP")
    ap.add_argument("--port", type=int, default=8281, help="游戏监听端口")
    ap.add_argument("--side", default="R", choices=["R", "L"], help="持续模式发送的设备侧")
    ap.add_argument("--rate", type=float, default=30.0, help="发包频率 Hz")
    ap.add_argument("--selftest", action="store_true", help="跑自检编排时序(R+L 双路)")
    args = ap.parse_args()
    if args.selftest:
        run_selftest(args)
    else:
        run_continuous(args)


if __name__ == "__main__":
    main()
