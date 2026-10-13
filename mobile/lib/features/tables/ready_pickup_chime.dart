import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:audioplayers/audioplayers.dart';

/// Short restaurant pickup melody (C6–E6–G6–C7) synthesized locally.
/// No internet, audio downloads, or copyrighted audio assets are required.
/// Playback is foreground/best-effort. OS notifications handle background.
class ReadyPickupChime {
  ReadyPickupChime({AudioPlayer Function()? playerFactory})
      : _playerFactory = playerFactory ?? AudioPlayer.new;

  final AudioPlayer Function() _playerFactory;
  Future<String>? _melodyFile;

  Future<void> play() async {
    final player = _playerFactory();
    try {
      final path = await (_melodyFile ??= _writeMelody());
      // Never await a sound forever or block the next kitchen alert.
      final finished = player.onPlayerComplete.first.timeout(
        const Duration(seconds: 6),
        onTimeout: () {},
      );
      await player.play(DeviceFileSource(path));
      await finished;
    } on Object {
      // A missing/muted audio device cannot interfere with meal delivery.
    } finally {
      await player.dispose();
    }
  }

  Future<String> _writeMelody() async {
    final file = File(
      '${Directory.systemTemp.path}${Platform.pathSeparator}restaurant_ready_pickup.wav',
    );
    if (await file.exists() && await file.length() > 44) return file.path;
    await file.writeAsBytes(generateReadyPickupWave(), flush: true);
    return file.path;
  }
}

/// Pure PCM generator, independently testable without a platform audio device.
Uint8List generateReadyPickupWave() {
  const sampleRate = 22050;
  const segmentSeconds = 0.24;
  const gapSeconds = 0.055;
  const frequencies = <double>[1046.5, 1318.5, 1568.0, 2093.0];
  final notes = (sampleRate * segmentSeconds).round();
  final silence = (sampleRate * gapSeconds).round();
  final total = frequencies.length * (notes + silence);
  final bytes = ByteData(44 + total * 2);
  void ascii(int index, String s) {
    for (var i = 0; i < s.length; i++) {
      bytes.setUint8(index + i, s.codeUnitAt(i));
    }
  }
  ascii(0, 'RIFF');
  bytes.setUint32(4, 36 + total * 2, Endian.little);
  ascii(8, 'WAVE');
  ascii(12, 'fmt ');
  bytes.setUint32(16, 16, Endian.little);
  bytes.setUint16(20, 1, Endian.little);
  bytes.setUint16(22, 1, Endian.little);
  bytes.setUint32(24, sampleRate, Endian.little);
  bytes.setUint32(28, sampleRate * 2, Endian.little);
  bytes.setUint16(32, 2, Endian.little);
  bytes.setUint16(34, 16, Endian.little);
  ascii(36, 'data');
  bytes.setUint32(40, total * 2, Endian.little);

  var offset = 44;
  for (final frequency in frequencies) {
    for (var n = 0; n < notes; n++) {
      final t = n / sampleRate;
      final envelope = pow(
        min(1.0, min(n / 275.0, (notes - n) / 950.0)).clamp(0.0, 1.0),
        1.3,
      ).toDouble();
      final value = (sin(2 * pi * frequency * t) +
          0.18 * sin(2 * pi * frequency * 2 * t));
      bytes.setInt16(
        offset,
        (value * envelope * 11000).round().clamp(-32768, 32767),
        Endian.little,
      );
      offset += 2;
    }
    for (var n = 0; n < silence; n++) {
      bytes.setInt16(offset, 0, Endian.little);
      offset += 2;
    }
  }
  return bytes.buffer.asUint8List();
}
