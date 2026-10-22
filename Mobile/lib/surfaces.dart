import 'dart:ui';

import 'package:flutter/material.dart';

const workspaceColor = Color(0xffedf0f5);
const officeBlue = Color(0xff185abd);

Future<String?> askColor(
  BuildContext context,
  String title, {
  String initial = '#185abd',
}) async {
  final input = TextEditingController(text: initial);
  String? error;
  final value = await showDialog<String>(
    context: context,
    builder: (context) => StatefulBuilder(
      builder: (context, update) => AlertDialog(
        title: Text(title),
        content: SizedBox(
          width: 290,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: [
                  for (final hex in [
                    '#242424',
                    '#ffffff',
                    '#185abd',
                    '#107c41',
                    '#c43e1c',
                    '#744da9',
                    '#dce8fa',
                    '#dff1e6',
                    '#fff1ba',
                    '#ffd8d2',
                    '#f1dffc',
                    '#64748b',
                  ])
                    Semantics(
                      label: hex,
                      button: true,
                      child: InkWell(
                        onTap: () => Navigator.pop(context, hex),
                        borderRadius: BorderRadius.circular(10),
                        child: Container(
                          width: 44,
                          height: 44,
                          decoration: BoxDecoration(
                            color: Color(
                              int.parse(hex.replaceFirst('#', 'ff'), radix: 16),
                            ),
                            borderRadius: BorderRadius.circular(10),
                            border: Border.all(color: const Color(0xffbcc8d8)),
                          ),
                        ),
                      ),
                    ),
                ],
              ),
              const SizedBox(height: 20),
              TextField(
                controller: input,
                decoration: InputDecoration(
                  labelText: 'Hex color',
                  hintText: '#185abd',
                  errorText: error,
                ),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              final hex = input.text.trim();
              if (RegExp(r'^#[0-9a-fA-F]{6}$').hasMatch(hex)) {
                Navigator.pop(context, hex);
              } else {
                update(() => error = 'Enter a color such as #185abd');
              }
            },
            child: const Text('Apply'),
          ),
        ],
      ),
    ),
  );
  // The route may still be animating out when showDialog completes.
  await Future<void>.delayed(const Duration(milliseconds: 250));
  input.dispose();
  return value;
}

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
      color: Theme.of(context).colorScheme.surface,
      borderRadius: BorderRadius.circular(radius),
      border: Border.all(
        color: Theme.of(context).colorScheme.outlineVariant
            .withValues(alpha: .6),
      ),
      boxShadow: raised
          ? [
              BoxShadow(
                color: Theme.of(context).brightness == Brightness.dark
                    ? const Color(0x66000000)
                    : const Color(0x22a1acc0),
                offset: const Offset(4, 4),
                blurRadius: 12,
              ),
              BoxShadow(
                color: Theme.of(context).brightness == Brightness.dark
                    ? const Color(0x224a5b72)
                    : const Color(0xddffffff),
                offset: const Offset(-4, -4),
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
    final surface = ColoredBox(
      color: Theme.of(context).colorScheme.surface.withValues(alpha: .86),
      child: child,
    );
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
