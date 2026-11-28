"""Independent minimal PRI and XBF 1 fixtures for the managed package reader."""
import struct
from pathlib import Path


def pri(path):
    def u16(*values):
        return struct.pack('<' + 'H' * len(values), *values)

    def u32(*values):
        return struct.pack('<' + 'I' * len(values), *values)

    def text(value):
        return (value + '\0').encode('utf-16le')

    names = ['', 'Resources', 'Hello', 'Blank', 'Label', 'Text']
    parents = [0, 0, 1, 1, 1, 4]
    payloads = [0, 1, 0, 1, 2, 2]
    scopes = {0, 1, 4}
    pool = bytearray()
    nodes = bytearray()
    for i, name in enumerate(names):
        nodes += u16(parents[i], 0, ord(name[:1].upper()) if name else 0)
        nodes += bytes([len(name), 16 if i in scopes else 0]) + u16(len(pool) // 2, payloads[i])
        pool += text(name)
    hnames = u16(32, 0) + u32(6, 3, 3, len(pool) // 2, 0)
    hnames += nodes + u16(0, 1, 1, 0, 1, 3, 2, 0, 4, 1, 5, 0) + u16(2, 3, 5) + pool
    hnames = hnames[:20] + u32(len(hnames)) + hnames[24:]
    schema = u16(1, 8, 8, 0) + u16(1, 0) + u32(0, 0, 3, 3) + text('Fixture') * 2
    schema += bytes(-len(schema) % 4) + hnames
    decisions = u16(0, 0, 1, 2, 1, 0) + u16(0, 0, 0, 1) + u16(0, 0) + u16(0)
    data = [text('đá 💎'), text(''), text('A label')]
    pool = b''.join(data)
    dataitems = u32(0) + u16(3, 0) + u32(len(pool))
    offset = 0
    for value in data:
        dataitems += u16(offset, len(value)); offset += len(value)
    dataitems += pool
    resource_map = u16(0, 0, 0, 0, 2, 1, 1, 1, 3, 0) + u32(3, 0, 0)
    resource_map += u32(4, 0) + u16(0, 0) + u16(3, 0)
    resource_map += b''.join(u16(1, i) for i in range(3))
    resource_map += b''.join(bytes([1, 0]) + u16(0, i, 3) for i in range(3))
    sections = [('[mrm_hschema]', schema), ('[mrm_res_map2_]', resource_map), ('[mrm_decn_info]', decisions), ('[mrm_dataitem]', dataitems)]
    toc = bytearray(); body = bytearray()
    for name, value in sections:
        kind = name.encode().ljust(16, b'\0')
        size = (32 + len(value) + 8 + 7) & ~7
        section = kind + u32(0) + u16(0, 0) + u32(size, 0) + value
        section += bytes(size - 8 - len(section)) + u32(0xdef5fade, size)
        toc += kind + u16(0, 0) + u32(0, len(body), size)
        body += section
    total = 32 + len(toc) + len(body) + 16
    header = b'mrm_pri1' + u16(0, 1) + u32(total, 32, 32 + len(toc)) + u16(len(sections), 0xffff) + u32(0)
    Path(path).write_bytes(header + toc + body + u32(0xdefffade, total) + b'mrm_pri1')




def xbf(path):
    strings = ['Page', 'Grid', 'UIElementCollection', 'Button', 'Content', 'Children', '__implicit_items', 'Name', 'ConnectionId', 'Content', 'Margin', 'Height', 'IsEnabled', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation', 'Windows.UI.Xaml.Controls', 'Windows.UI.Xaml', 'System.Windows']
    def words(*values): return struct.pack('<' + 'I' * len(values), *values)
    def text(value):
        encoded = value.encode('utf-16le')
        return words(len(encoded) // 2) + encoded
    def record(op, index, flags=0): return bytes([op]) + words(index, flags)
    def prop(index, value): return record(3, index) + value + b'\4'
    def value(kind, data=b''): return bytes([6, kind]) + data
    metadata = bytearray(120); metadata[:8] = words(1, 0)
    tables = [words(len(strings)) + b''.join(text(s) for s in strings), words(1, 1, 16), words(2, 0, 14, 0, 15),
              words(4) + b''.join(words(0, 0, i) for i in range(4)),
              words(9) + b''.join(words(flags, owner, index) for flags, owner, index in [(0, 0, 4), (0, 1, 5), (4, 0, 6), (2, 0, 7), (2, 0, 8), (0, 3, 9), (0, 3, 10), (0, 3, 11), (0, 3, 12)]), words(1, 13)]
    for i, table in enumerate(tables):
        struct.pack_into('<Q', metadata, 8 + i * 8, len(metadata)); metadata += table
    nodes = record(7, 0) + text('') + record(1, 0) + record(3, 0) + record(1, 1)
    nodes += record(3, 1) + record(1, 2, 1) + record(3, 2) + record(1, 3)
    nodes += prop(3, value(5, text('title'))) + prop(4, value(4, struct.pack('<i', 37)))
    nodes += prop(5, value(5, text('đá 💎'))) + prop(6, value(7, struct.pack('<4f', 1, 2, 3, 4)))
    nodes += prop(7, value(3, struct.pack('<f', 42.5))) + prop(8, value(1))
    nodes += b'\2\4\2\4\2\4\2'
    Path(path).write_bytes(b'XBF\0' + words(len(metadata), len(nodes)) + metadata + nodes)




class Xbf:
    """Serialize the public XBF 1 tables and scopes, independently of the reader."""
    def __init__(self):
        self.strings = []
        self.namespaces = []
        self.types = []
        self.properties = []
        self.nodes = bytearray(bytes([7]) + struct.pack("<III", 0, 0, 0))
    @staticmethod
    def words(*values): return struct.pack('<' + 'I' * len(values), *values)
    def string(self, value):
        if value not in self.strings: self.strings.append(value)
        return self.strings.index(value)
    def type(self, name, namespace='Windows.UI.Xaml.Controls'):
        if namespace not in self.namespaces: self.namespaces.append(namespace)
        value = (0, self.namespaces.index(namespace), self.string(name))
        if value not in self.types: self.types.append(value)
        return self.types.index(value)
    def property(self, owner, name, flags=0):
        value = (flags, owner, self.string(name))
        if value not in self.properties: self.properties.append(value)
        return self.properties.index(value)
    def record(self, op, *values): self.nodes += bytes([op]) + self.words(*values)
    def obj(self, kind, flags=0): self.record(1, kind, flags)
    def prop(self, owner, name, flags=0): self.record(3, self.property(owner, name, flags), 0)
    def text(self, value):
        encoded = value.encode('utf-16le'); self.nodes += bytes([6, 5]) + self.words(len(encoded) // 2) + encoded
    def attribute(self, owner, name, value, flags=0):
        self.prop(owner, name, flags); self.text(value); self.record(4)
    def endobj(self): self.record(2)
    def endprop(self): self.record(4)
    def write(self, path):
        for ns in self.namespaces: self.string(ns)
        self.string('System.Windows')
        presentation = self.string('http://schemas.microsoft.com/winfx/2006/xaml/presentation')
        def text(value):
            encoded = value.encode('utf-16le'); return self.words(len(encoded) // 2) + encoded
        tables = [self.words(len(self.strings)) + b''.join(text(s) for s in self.strings), self.words(1, 1, self.string('System.Windows')),
                  self.words(len(self.namespaces)) + b''.join(self.words(0, self.string(ns)) for ns in self.namespaces),
                  self.words(len(self.types)) + b''.join(self.words(*entry) for entry in self.types),
                  self.words(len(self.properties)) + b''.join(self.words(*entry) for entry in self.properties), self.words(1, presentation)]
        metadata = bytearray(120); metadata[:8] = self.words(1, 0)
        for i, table in enumerate(tables):
            struct.pack_into('<Q', metadata, 8 + i * 8, len(metadata)); metadata += table
        Path(path).write_bytes(b'XBF\0' + self.words(len(metadata), len(self.nodes)) + metadata + self.nodes)


def templates(path):
    x = Xbf()
    page, dictionary, style, setters, setter, template, border, button, resource, binding, primitive = [x.type(n) for n in
        ['Page', 'ResourceDictionary', 'Style', 'SetterBaseCollection', 'Setter', 'ControlTemplate', 'Border', 'Button', 'StaticResource', 'Binding', 'String']]
    x.types[primitive] = (0, len(x.namespaces), x.string('String')); x.namespaces.append('Windows.Foundation')
    x.obj(page)
    x.prop(page, 'Resources'); x.obj(dictionary)
    x.prop(dictionary, '__implicit_items', 4)
    x.obj(primitive); x.attribute(primitive, 'Key', 'Word', 2); x.attribute(primitive, '__implicit_initialization', 'template text', 4); x.endobj()
    x.obj(style); x.attribute(style, 'Key', 'Probe', 2); x.attribute(style, 'TargetType', 'Button')
    x.prop(style, 'Setters'); x.obj(setters, 1); x.prop(setters, '__implicit_items', 4)
    x.obj(setter); x.attribute(setter, 'Property', 'Template'); x.prop(setter, 'Value')
    x.obj(template); x.attribute(template, 'TargetType', 'Button'); x.prop(template, 'Template')
    x.obj(border); x.attribute(border, 'Name', 'face', 2); x.attribute(border, 'Background', 'Red'); x.endobj()
    x.endprop(); x.endobj(); x.endprop(); x.endobj()
    x.endprop(); x.endobj(); x.endprop(); x.endobj()
    x.endprop(); x.endobj(); x.endprop()
    x.prop(page, 'Content'); x.obj(button); x.attribute(button, 'Name', 'styled', 2)
    x.prop(button, 'Style'); x.obj(resource); x.attribute(resource, 'ResourceKey', 'Probe'); x.endobj(); x.endprop()
    x.prop(button, 'Content'); x.obj(binding); x.attribute(binding, 'Path', 'Label'); x.endobj(); x.endprop()
    x.endobj(); x.endprop(); x.endobj()
    x.write(path)




def inheritance(path):
    x = Xbf()
    page, grid, panel, collection, button = [x.type(n) for n in ['Page', 'Grid', 'Panel', 'UIElementCollection', 'Button']]
    x.obj(page); x.prop(page, 'Content'); x.obj(grid)
    x.attribute(panel, 'Background', 'Red')
    x.prop(panel, 'Children'); x.obj(collection, 1); x.prop(collection, '__implicit_items', 4)
    x.obj(button); x.attribute(button, 'Name', 'rowButton', 2); x.attribute(grid, 'Row', '2'); x.endobj()
    x.endprop(); x.endobj(); x.endprop(); x.endobj(); x.endprop(); x.endobj()
    x.write(path)




def late_control(path):
    x = Xbf()
    page, control = x.type('Page'), x.type('LateControl', 'Fixture')
    x.obj(page); x.prop(page, 'Content'); x.obj(control); x.attribute(control, 'Score', '17'); x.endobj(); x.endprop(); x.endobj()
    x.write(path)


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pri_path', type=Path)
    parser.add_argument('xbf_path', type=Path, nargs='?')
    args = parser.parse_args()
    pri(args.pri_path)
    if args.xbf_path:
        xbf(args.xbf_path)
        templates(args.xbf_path.with_name('Templates.xbf'))
        inheritance(args.xbf_path.with_name('Inheritance.xbf'))
        late_control(args.xbf_path.with_name('LateControl.xbf'))
