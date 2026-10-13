import 'dart:math' as math;

import 'package:flutter/material.dart';

/// A compact overhead floor-plan view: tabletop, chairs and capacity.
/// No network images are needed and the shape remains clear offline.
class RestaurantTableVisual extends StatelessWidget {
  const RestaurantTableVisual({
    required this.name,
    required this.capacity,
    required this.status,
    super.key,
  });

  final String name;
  final int capacity;
  final String status;

  static Color statusColor(String status) => switch (status) {
        'available' => const Color(0xFF159A65),
        'occupied' => const Color(0xFFD97706),
        'reserved' => const Color(0xFF2563EB),
        _ => const Color(0xFF64748B),
      };

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: '$name, $capacity seats, $status',
      image: true,
      child: SizedBox(
        height: 108,
        width: double.infinity,
        child: CustomPaint(
          painter: RestaurantTablePainter(
            capacity: capacity,
            color: statusColor(status),
          ),
        ),
      ),
    );
  }
}

@immutable
class RestaurantTablePainter extends CustomPainter {
  const RestaurantTablePainter({
    required this.capacity,
    required this.color,
  });

  final int capacity;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final round = capacity <= 4;
    final visibleSeats = capacity.clamp(1, 10);
    final chairPaint = Paint()
      ..style = PaintingStyle.fill
      ..color = color.withValues(alpha: .37);
    final chairOutline = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5
      ..color = color;
    final boardPaint = Paint()
      ..color = color.withValues(alpha: .16)
      ..style = PaintingStyle.fill;
    final boardOutline = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2.5;
    final horizontalRadius = math.min(size.width * .34, round ? 56.0 : 71.0);
    final verticalRadius = math.min(size.height * .39, 42.0);

    for (var i = 0; i < visibleSeats; i++) {
      final angle = -math.pi / 2 + i * (2 * math.pi / visibleSeats);
      final seatCenter = Offset(
        center.dx + horizontalRadius * math.cos(angle),
        center.dy + verticalRadius * math.sin(angle),
      );
      canvas.drawCircle(seatCenter, 8, chairPaint);
      canvas.drawCircle(seatCenter, 8, chairOutline);
    }

    final width = math.min(size.width * .7, round ? 90.0 : 118.0);
    final bounds = Rect.fromCenter(
      center: center,
      width: width,
      height: 66,
    );
    final radius = Radius.circular(round ? 36 : 17);
    final tabletop = RRect.fromRectAndRadius(bounds, radius);
    canvas.drawRRect(tabletop, boardPaint);
    canvas.drawRRect(tabletop, boardOutline);

    final label = TextPainter(
      text: TextSpan(
        text: '$capacity seats',
        style: TextStyle(
          fontWeight: FontWeight.w700,
          fontSize: 13,
          color: color,
        ),
      ),
      textDirection: TextDirection.ltr,
    )..layout(maxWidth: width - 8);
    label.paint(
      canvas,
      Offset(center.dx - label.width / 2, center.dy - label.height / 2),
    );
  }

  @override
  bool shouldRepaint(covariant RestaurantTablePainter oldDelegate) =>
      oldDelegate.capacity != capacity || oldDelegate.color != color;
}
