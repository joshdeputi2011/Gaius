import 'dart:ui';

import 'package:flutter/material.dart';

const workspaceColor = Color(0xffedf0f5);
const officeBlue = Color(0xff185abd);

/// Soft raised surfaces use two shallow shadows; blur is confined to app chrome.
class SoftSurface extends StatelessWidget {
  const SoftSurface({
    super.key,
    required this.child,
    this.padding = EdgeInsets.zero,
    this.radius = 18,
    this.raised = true,
  });
  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final bool raised;
  @override
  Widget build(BuildContext context) => Container(
    padding: padding,
    decoration: BoxDecoration(
      color: workspaceColor,
      borderRadius: BorderRadius.circular(radius),
      border: Border.all(color: Colors.white.withValues(alpha: .8)),
      boxShadow: raised
          ? const [
              BoxShadow(
                color: Color(0x22a1acc0),
                offset: Offset(4, 4),
                blurRadius: 12,
              ),
              BoxShadow(
                color: Color(0xddffffff),
                offset: Offset(-4, -4),
                blurRadius: 12,
              ),
            ]
          : null,
    ),
    child: child,
  );
}

class GlassSurface extends StatelessWidget {
  const GlassSurface({super.key, required this.child});
  final Widget child;
  @override
  Widget build(BuildContext context) {
    final surface = ColoredBox(color: const Color(0xe6f7f9fc), child: child);
    if (MediaQuery.disableAnimationsOf(context)) return surface;
    return ClipRect(
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 12, sigmaY: 12),
        child: surface,
      ),
    );
  }
}

class RibbonCommand extends StatelessWidget {
  const RibbonCommand({
    super.key,
    required this.icon,
    required this.label,
    required this.onPressed,
    this.color = officeBlue,
  });
  final IconData icon;
  final String label;
  final VoidCallback? onPressed;
  final Color color;
  @override
  Widget build(BuildContext context) => SizedBox(
    width: 82,
    child: TextButton(
      style: TextButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
      ),
      onPressed: onPressed,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 24, color: onPressed == null ? Colors.grey : color),
          const SizedBox(height: 7),
          Text(
            label,
            maxLines: 2,
            textAlign: TextAlign.center,
            style: const TextStyle(fontSize: 11, height: 1.15),
          ),
        ],
      ),
    ),
  );
}
