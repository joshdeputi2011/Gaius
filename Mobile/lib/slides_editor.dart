import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:fluentui_system_icons/fluentui_system_icons.dart';
import 'package:uuid/uuid.dart';

import 'models.dart';
import 'editors.dart' show EditorToolbar, pickBytes;
import 'office_chrome.dart';
import 'surfaces.dart';

Color slideColor(String value) =>
    Color(int.parse(value.replaceFirst('#', 'ff'), radix: 16));

class SlidesEditor extends StatefulWidget {
  const SlidesEditor({super.key, required this.file, required this.onChanged});
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged;
  @override
  State<SlidesEditor> createState() => _SlidesEditorState();
}

class _SlidesEditorState extends State<SlidesEditor> {
  int index = 0;
  String? selected;
  bool notes = false;
  Map<String, dynamic> get slide =>
      Map<String, dynamic>.from(widget.file.slides[index] as Map);
  void patch(Map<String, dynamic> values) {
    widget.file.slides[index] = {...slide, ...values};
    widget.onChanged(widget.file);
    setState(() {});
  }

  void patchObject(Map<String, dynamic> values) {
    patch({
      'objects': [
        for (final raw in slide['objects'] as List? ?? [])
          if (raw['id'] == selected)
            {...Map<String, dynamic>.from(raw as Map), ...values}
          else
            raw,
      ],
    });
  }

  void add() {
    widget.file.slides.add({
      'id': const Uuid().v4(),
      'title': 'New slide',
      'body': 'Add your content',
      'background': '#ffffff',
      'objects': <dynamic>[],
    });
    setState(() => index = widget.file.slides.length - 1);
    widget.onChanged(widget.file);
  }

  void duplicate() {
    final copy = jsonDecode(jsonEncode(slide)) as Map<String, dynamic>;
    copy['id'] = const Uuid().v4();
    widget.file.slides.insert(index + 1, copy);
    setState(() => index++);
    widget.onChanged(widget.file);
  }

  void object(String kind, {String? data}) {
    final id = const Uuid().v4();
    patch({
      'objects': [
        ...(slide['objects'] as List? ?? []),
        {
          'id': id,
          'kind': kind,
          'x': 15.0,
          'y': 60.0,
          'width': 30.0,
          'height': 22.0,
          'color': '#185abd',
          'fontSize': 32,
          'text': kind == 'wordart'
              ? 'Your WordArt'
              : kind == 'text'
              ? 'Text box'
              : '',
          'dataUrl': data,
          'animation': 'none',
        },
      ],
    });
    setState(() => selected = id);
  }

  Future<void> picture() async {
    final bytes = await pickBytes(['png', 'jpg', 'jpeg']);
    if (bytes != null && mounted) {
      object(
        'image',
        data:
            'data:image/${bytes.length > 4 && bytes[0] == 137 ? 'png' : 'jpeg'};base64,${base64Encode(bytes)}',
      );
    }
  }

  void present({bool start = false}) {
    Navigator.push(
      context,
      MaterialPageRoute<void>(
        builder: (_) => SlideShow(
          slides: List<Map<String, dynamic>>.from(widget.file.slides),
          initialIndex: start ? 0 : index,
        ),
      ),
    );
  }

  RibbonCommand cmd(String label, IconData icon, VoidCallback? action) =>
      RibbonCommand(
        label: label,
        icon: icon,
        onPressed: action,
        color: const Color(0xffc43e1c),
      );
  @override
  Widget build(BuildContext context) {
    if (widget.file.slides.isEmpty) {
      return Center(
        child: FilledButton(onPressed: add, child: const Text('Add a slide')),
      );
    }
    index = index.clamp(0, widget.file.slides.length - 1);
    final s = slide;
    final objects = (s['objects'] as List? ?? [])
            .map((o) => Map<String, dynamic>.from(o as Map))
            .toList(),
        active = objects.where((o) => o['id'] == selected).firstOrNull;
    return Column(
      children: [
        EditorToolbar(
          tabs: {
            'Insert': [
              cmd(
                'Text box',
                FluentIcons.text_font_24_regular,
                () => object('text'),
              ),
              cmd(
                'WordArt',
                FluentIcons.text_font_24_regular,
                () => object('wordart'),
              ),
              cmd('Picture', FluentIcons.image_24_regular, picture),
              cmd(
                'Rectangle',
                FluentIcons.shapes_24_regular,
                () => object('rectangle'),
              ),
              cmd(
                'Ellipse',
                FluentIcons.shapes_24_regular,
                () => object('ellipse'),
              ),
              cmd(
                'Triangle',
                FluentIcons.shapes_24_regular,
                () => object('triangle'),
              ),
            ],
            'Design': [
              for (final theme in {
                'Paper': '#ffffff',
                'Mist': '#e8f1fc',
                'Sage': '#e6f1eb',
                'Sand': '#fff2dd',
                'Rose': '#f8e8ec',
              }.entries)
                cmd(
                  theme.key,
                  FluentIcons.color_24_regular,
                  () => patch({'background': theme.value}),
                ),
              cmd('Custom color', FluentIcons.color_24_regular, () async {
                final color = await askText(
                  context,
                  'Background (#RRGGBB)',
                  initial: s['background'] as String,
                );
                if (color != null &&
                    RegExp(r'^#[a-fA-F0-9]{6}$').hasMatch(color)) {
                  patch({'background': color});
                }
              }),
            ],
            'Transitions': [
              for (final transition in [
                'none',
                'fade',
                'push',
                'zoom',
                'morph',
              ])
                cmd(
                  transition,
                  FluentIcons.slide_text_24_regular,
                  () => patch({'transition': transition}),
                ),
              cmd('Preview', FluentIcons.play_24_regular, present),
            ],
            'Animations': [
              for (final animation in ['none', 'fade', 'fly', 'zoom'])
                cmd(
                  animation,
                  FluentIcons.play_24_regular,
                  active == null
                      ? null
                      : () => patchObject({'animation': animation}),
                ),
              cmd('Preview', FluentIcons.play_24_regular, present),
            ],
            'Slide Show': [
              cmd(
                'From start',
                FluentIcons.play_24_regular,
                () => present(start: true),
              ),
              cmd('Current slide', FluentIcons.play_24_regular, present),
              cmd('Advance timing', FluentIcons.clock_24_regular, () async {
                final seconds = await askText(
                  context,
                  'Seconds per slide',
                  initial: '${s['duration'] ?? 5}',
                );
                final n = int.tryParse(seconds ?? '');
                if (n != null) patch({'duration': n.clamp(1, 120)});
              }),
            ],
            'Review': [
              cmd(
                'Speaker notes',
                FluentIcons.comment_24_regular,
                () => setState(() => notes = !notes),
              ),
              cmd(
                'Search',
                FluentIcons.search_24_regular,
                () => OfficeActions.of(context)?.search(),
              ),
            ],
            'View': [
              cmd(
                'Notes',
                FluentIcons.comment_24_regular,
                () => setState(() => notes = !notes),
              ),
              cmd(
                'Print preview',
                FluentIcons.print_24_regular,
                () => OfficeActions.of(context)?.printPreview(),
              ),
            ],
          },
          children: [
            cmd('New slide', FluentIcons.add_24_regular, add),
            cmd('Duplicate', FluentIcons.copy_24_regular, duplicate),
            cmd(
              'Delete slide',
              FluentIcons.delete_24_regular,
              widget.file.slides.length < 2
                  ? null
                  : () {
                      widget.file.slides.removeAt(index);
                      setState(
                        () => index = index.clamp(
                          0,
                          widget.file.slides.length - 1,
                        ),
                      );
                      widget.onChanged(widget.file);
                    },
            ),
            cmd('Present', FluentIcons.play_24_regular, present),
          ],
        ),
        SizedBox(
          height: 52,
          child: ListView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: 8),
            children: [
              for (var i = 0; i < widget.file.slides.length; i++)
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 4,
                    vertical: 7,
                  ),
                  child: ChoiceChip(
                    label: Text(
                      '${i + 1}. ${widget.file.slides[i]['title']}',
                      overflow: TextOverflow.ellipsis,
                    ),
                    selected: index == i,
                    onSelected: (_) => setState(() {
                      index = i;
                      selected = null;
                    }),
                  ),
                ),
            ],
          ),
        ),
        Expanded(
          child: ListView(
            padding: const EdgeInsets.all(12),
            children: [
              AspectRatio(
                aspectRatio: 16 / 9,
                child: SlideCanvas(
                  slide: s,
                  selected: selected,
                  onSelect: (id) => setState(() => selected = id),
                  onMove: (id, x, y) {
                    selected = id;
                    patchObject({'x': x, 'y': y});
                  },
                ),
              ),
              const SizedBox(height: 14),
              TextFormField(
                key: ValueKey('${s['id']}-title'),
                initialValue: s['title'] as String,
                decoration: const InputDecoration(labelText: 'Slide title'),
                onChanged: (value) => patch({'title': value}),
              ),
              const SizedBox(height: 10),
              TextFormField(
                key: ValueKey('${s['id']}-body'),
                initialValue: s['body'] as String,
                minLines: 2,
                maxLines: 5,
                decoration: const InputDecoration(labelText: 'Slide body'),
                onChanged: (value) => patch({'body': value}),
              ),
              if (notes) ...[
                const SizedBox(height: 10),
                TextFormField(
                  key: ValueKey('${s['id']}-notes'),
                  initialValue: s['notes'] as String? ?? '',
                  minLines: 2,
                  maxLines: 6,
                  decoration: const InputDecoration(labelText: 'Speaker notes'),
                  onChanged: (value) => patch({'notes': value}),
                ),
              ],
              if (active != null) ...[
                const SizedBox(height: 14),
                SoftSurface(
                  padding: const EdgeInsets.all(14),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Format ${active['kind']}',
                        style: const TextStyle(fontWeight: FontWeight.w600),
                      ),
                      if (['text', 'wordart'].contains(active['kind']))
                        TextFormField(
                          key: ValueKey('${active['id']}-text'),
                          initialValue: active['text'] as String? ?? '',
                          decoration: const InputDecoration(
                            labelText: 'Object text',
                          ),
                          onChanged: (v) => patchObject({'text': v}),
                        ),
                      Wrap(
                        spacing: 6,
                        children: [
                          for (final name in ['x', 'y', 'width', 'height'])
                            SizedBox(
                              width: 90,
                              child: TextFormField(
                                key: ValueKey(
                                  '${active['id']}-$name-${active[name]}',
                                ),
                                initialValue:
                                    '${(active[name] as num).round()}',
                                keyboardType: TextInputType.number,
                                decoration: InputDecoration(
                                  labelText: '$name %',
                                ),
                                onFieldSubmitted: (v) {
                                  final n = double.tryParse(v);
                                  if (n != null) {
                                    patchObject({
                                      name: n.clamp(
                                        name == 'width' || name == 'height'
                                            ? 1
                                            : 0,
                                        100,
                                      ),
                                    });
                                  }
                                },
                              ),
                            ),
                        ],
                      ),
                      TextButton(
                        onPressed: () {
                          patch({
                            'objects': objects
                                .where((o) => o['id'] != selected)
                                .toList(),
                          });
                          setState(() => selected = null);
                        },
                        child: const Text('Delete object'),
                      ),
                    ],
                  ),
                ),
              ],
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 5),
          child: Row(
            children: [
              Text(
                'Slide ${index + 1} of ${widget.file.slides.length}',
                style: const TextStyle(fontSize: 10),
              ),
              const Spacer(),
              Text(
                '${s['transition'] ?? 'none'} transition',
                style: const TextStyle(fontSize: 10),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class SlideCanvas extends StatelessWidget {
  const SlideCanvas({
    super.key,
    required this.slide,
    this.selected,
    this.onSelect,
    this.onMove,
    this.presenting = false,
  });
  final Map<String, dynamic> slide;
  final String? selected;
  final ValueChanged<String?>? onSelect;
  final void Function(String, double, double)? onMove;
  final bool presenting;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, size) {
      final morph =
              slide['transition'] == 'morph' &&
              !MediaQuery.disableAnimationsOf(context),
          duration = Duration(milliseconds: morph ? 650 : 0);
      return GestureDetector(
        onTap: () => onSelect?.call(null),
        child: AnimatedContainer(
          duration: duration,
          color: slideColor(slide['background'] as String),
          child: ClipRect(
            child: Stack(
              children: [
                Positioned(
                  left: size.maxWidth * .07,
                  top: size.maxHeight * .22,
                  width: size.maxWidth * .86,
                  child: Text(
                    slide['title'] as String,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: const Color(0xff24334a),
                      fontSize: size.maxWidth * .047,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
                Positioned(
                  left: size.maxWidth * .07,
                  top: size.maxHeight * .45,
                  width: size.maxWidth * .86,
                  child: Text(
                    slide['body'] as String,
                    style: TextStyle(
                      color: const Color(0xff586b83),
                      fontSize: size.maxWidth * .027,
                    ),
                  ),
                ),
                for (final raw in slide['objects'] as List? ?? [])
                  Builder(
                    key: ValueKey(raw['id']),
                    builder: (_) {
                      final o = Map<String, dynamic>.from(raw as Map),
                          kind = o['kind'] as String,
                          color = slideColor(
                            o['color'] as String? ?? '#185abd',
                          );
                      Widget child;
                      if (kind == 'image' && o['dataUrl'] is String) {
                        child = Image.memory(
                          base64Decode(
                            (o['dataUrl'] as String).split(',').last,
                          ),
                          fit: BoxFit.contain,
                        );
                      } else if ([
                        'rectangle',
                        'ellipse',
                        'triangle',
                      ].contains(kind)) {
                        child = kind == 'triangle'
                            ? CustomPaint(painter: _Triangle(color))
                            : Container(
                                decoration: BoxDecoration(
                                  color: color,
                                  borderRadius: kind == 'ellipse'
                                      ? BorderRadius.circular(999)
                                      : null,
                                ),
                              );
                      } else {
                        child = Center(
                          child: Text(
                            o['text'] as String? ?? '',
                            textAlign: TextAlign.center,
                            style: TextStyle(
                              fontSize:
                                  (o['fontSize'] as num? ?? 32) *
                                  size.maxWidth /
                                  960,
                              color: color,
                              fontWeight: kind == 'wordart'
                                  ? FontWeight.w800
                                  : FontWeight.normal,
                              shadows: kind == 'wordart'
                                  ? [
                                      const Shadow(
                                        color: Color(0xffb4c9e5),
                                        offset: Offset(1.5, 2),
                                      ),
                                    ]
                                  : null,
                            ),
                          ),
                        );
                      }
                      if (presenting && !morph && o['animation'] != 'none') {
                        child = TweenAnimationBuilder<double>(
                          key: ValueKey('${slide['id']}-${o['id']}'),
                          tween: Tween(begin: 0, end: 1),
                          duration: MediaQuery.disableAnimationsOf(context)
                              ? Duration.zero
                              : const Duration(milliseconds: 700),
                          builder: (_, v, c) => Opacity(
                            opacity: v,
                            child: Transform.translate(
                              offset: Offset(
                                o['animation'] == 'fly' ? (1 - v) * 60 : 0,
                                0,
                              ),
                              child: Transform.scale(
                                scale: o['animation'] == 'zoom'
                                    ? 0.7 + 0.3 * v
                                    : 1,
                                child: c,
                              ),
                            ),
                          ),
                          child: child,
                        );
                      }
                      return AnimatedPositioned(
                        duration: duration,
                        left: (o['x'] as num) * size.maxWidth / 100,
                        top: (o['y'] as num) * size.maxHeight / 100,
                        width: (o['width'] as num) * size.maxWidth / 100,
                        height: (o['height'] as num) * size.maxHeight / 100,
                        child: GestureDetector(
                          onTap: () => onSelect?.call(o['id'] as String),
                          onPanUpdate: onMove == null
                              ? null
                              : (event) => onMove!(
                                  o['id'] as String,
                                  ((o['x'] as num) +
                                          event.delta.dx / size.maxWidth * 100)
                                      .clamp(0, 100 - (o['width'] as num))
                                      .toDouble(),
                                  ((o['y'] as num) +
                                          event.delta.dy / size.maxHeight * 100)
                                      .clamp(0, 100 - (o['height'] as num))
                                      .toDouble(),
                                ),
                          child: Container(
                            decoration: selected == o['id']
                                ? BoxDecoration(
                                    border: Border.all(
                                      color: officeBlue,
                                      width: 2,
                                    ),
                                  )
                                : null,
                            child: child,
                          ),
                        ),
                      );
                    },
                  ),
              ],
            ),
          ),
        ),
      );
    },
  );
}

class _Triangle extends CustomPainter {
  _Triangle(this.color);
  final Color color;
  @override
  void paint(Canvas canvas, Size size) {
    canvas.drawPath(
      Path()
        ..moveTo(size.width / 2, 0)
        ..lineTo(size.width, size.height)
        ..lineTo(0, size.height)
        ..close(),
      Paint()..color = color,
    );
  }

  @override
  bool shouldRepaint(_Triangle old) => old.color != color;
}

class SlideShow extends StatefulWidget {
  const SlideShow({
    super.key,
    required this.slides,
    required this.initialIndex,
  });
  final List<Map<String, dynamic>> slides;
  final int initialIndex;
  @override
  State<SlideShow> createState() => _SlideShowState();
}

class _SlideShowState extends State<SlideShow> {
  late int index = widget.initialIndex;
  bool auto = false;
  Timer? timer;
  void go(int direction) {
    setState(
      () => index = (index + direction).clamp(0, widget.slides.length - 1),
    );
    schedule();
  }

  void schedule() {
    timer?.cancel();
    if (auto) {
      timer = Timer(
        Duration(
          seconds: (widget.slides[index]['duration'] as int? ?? 5).clamp(
            1,
            120,
          ),
        ),
        () => go(
          index < widget.slides.length - 1 ? 1 : -(widget.slides.length - 1),
        ),
      );
    }
  }

  @override
  void dispose() {
    timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final slide = widget.slides[index],
        type = slide['transition'] as String? ?? 'none';
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text('${index + 1} / ${widget.slides.length}'),
        actions: [
          IconButton(
            tooltip: 'Automatic advance',
            onPressed: () {
              setState(() => auto = !auto);
              schedule();
            },
            icon: Icon(auto ? Icons.pause : Icons.play_arrow),
          ),
        ],
      ),
      body: Center(
        child: GestureDetector(
          onTap: () => go(1),
          onHorizontalDragEnd: (e) => go((e.primaryVelocity ?? 0) < 0 ? 1 : -1),
          child: AspectRatio(
            aspectRatio: 16 / 9,
            child: AnimatedSwitcher(
              duration:
                  type == 'none' ||
                      type == 'morph' ||
                      MediaQuery.disableAnimationsOf(context)
                  ? Duration.zero
                  : const Duration(milliseconds: 600),
              transitionBuilder: (child, animation) => type == 'push'
                  ? SlideTransition(
                      position: Tween(
                        begin: const Offset(1, 0),
                        end: Offset.zero,
                      ).animate(animation),
                      child: child,
                    )
                  : type == 'zoom'
                  ? ScaleTransition(scale: animation, child: child)
                  : FadeTransition(opacity: animation, child: child),
              child: SlideCanvas(
                key: ValueKey(type == 'morph' ? 'morph' : slide['id']),
                slide: slide,
                presenting: true,
              ),
            ),
          ),
        ),
      ),
      bottomNavigationBar: SafeArea(
        child: Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            IconButton(
              tooltip: 'Previous slide',
              onPressed: index == 0 ? null : () => go(-1),
              icon: const Icon(Icons.chevron_left, color: Colors.white),
            ),
            const Text(
              'Swipe or tap to advance',
              style: TextStyle(color: Colors.white70, fontSize: 12),
            ),
            IconButton(
              tooltip: 'Next slide',
              onPressed: index == widget.slides.length - 1 ? null : () => go(1),
              icon: const Icon(Icons.chevron_right, color: Colors.white),
            ),
          ],
        ),
      ),
    );
  }
}
