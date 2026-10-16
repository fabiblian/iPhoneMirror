"""Device clipboard image sync; no physical device or desktop access."""
import base64
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
import usb_touch_bridge as bridge

PNG = b'\x89PNG\r\n\x1a\n' + b'pixels' * 10


class Ipc:
    def __init__(self):
        self.events = []

    async def emit(self, event):
        self.events.append(event)


class Board:
    """Shared fake device pasteboard behind fresh service connections."""

    def __init__(self, items):
        self.items = items
        self.policies = []
        self.sets = []


def service_for(board):
    class Service:
        def __init__(self, rsd):
            self.rsd = rsd

        async def __aenter__(self):
            return self

        async def __aexit__(self, *_args):
            pass

        async def get(self, data_policy=None):
            board.policies.append(data_policy)
            return {'items': board.items(data_policy) if callable(board.items) else board.items}

        async def set(self, items):
            board.sets.append(items)
            board.items = items

        async def set_text(self, text):
            board.items = [{'types': ['public.utf8-plain-text'],
                            'data': {'public.utf8-plain-text': {'data': text.encode()}}}]

    return Service


def image_items(data=PNG, uti='public.png'):
    return [{'types': [uti], 'data': {uti: {'data': data}}}]


class ClipboardImageTests(unittest.IsolatedAsyncioTestCase):
    def session(self):
        session = bridge.TouchSession(Ipc(), 120, 'clipboard-image-test', transport='usb')
        session.rsd = object()
        return session

    def images(self, session):
        return [e for e in session.ipc.events if e.get('event') == 'clipboard_image']

    async def test_image_board_is_published_once(self):
        session = self.session()
        board = Board(image_items())
        with patch.object(bridge, 'PasteboardService', service_for(board)):
            await session._publish_device_pasteboard()
            await session._publish_device_pasteboard()
        images = self.images(session)
        self.assertEqual(1, len(images))
        self.assertEqual('public.png', images[0]['uti'])
        self.assertEqual(PNG, base64.b64decode(images[0]['data']))
        self.assertFalse(any(e.get('event') == 'clipboard_text' for e in session.ipc.events))

    async def test_text_wins_over_image(self):
        session = self.session()
        items = image_items() + [{'types': ['public.utf8-plain-text'],
                                  'data': {'public.utf8-plain-text': {'data': b'caption'}}}]
        with patch.object(bridge, 'PasteboardService', service_for(Board(items))):
            await session._publish_device_pasteboard()
        self.assertEqual([], self.images(session))
        texts = [e['text'] for e in session.ipc.events if e.get('event') == 'clipboard_text']
        self.assertEqual(['caption'], texts)

    async def test_host_image_write_does_not_echo_back(self):
        session = self.session()
        board = Board([])
        with patch.object(bridge, 'PasteboardService', service_for(board)):
            await session._write_device_pasteboard_image('public.png', PNG)
            await session._publish_device_pasteboard()
        self.assertEqual(1, len(board.sets))
        self.assertEqual([], self.images(session))

    async def test_same_image_after_text_is_new_again(self):
        session = self.session()
        board = Board(image_items())
        with patch.object(bridge, 'PasteboardService', service_for(board)):
            await session._publish_device_pasteboard()
            board.items = [{'types': ['public.utf8-plain-text'],
                            'data': {'public.utf8-plain-text': {'data': b'x'}}}]
            await session._publish_device_pasteboard()
            board.items = image_items()
            await session._publish_device_pasteboard()
        self.assertEqual(2, len(self.images(session)))

    async def test_sized_promise_is_resolved_once(self):
        session = self.session()

        def items(policy):
            if policy == {'allResolved': {}}:
                return image_items(uti='public.jpeg', data=b'jpeg')
            return [{'types': ['public.jpeg'],
                     'data': {'public.jpeg': {'isPromised': True, 'size': 4}}}]

        board = Board(items)
        with patch.object(bridge, 'PasteboardService', service_for(board)):
            for _ in range(3):
                await session._publish_device_pasteboard()
        self.assertEqual(1, board.policies.count({'allResolved': {}}))
        self.assertEqual(1, len(self.images(session)))

    async def test_too_large_image_is_reported_not_sent(self):
        session = self.session()
        with patch.object(bridge, 'MAX_DEVICE_CLIPBOARD_IMAGE_BYTES', 10), \
                patch.object(bridge, 'PasteboardService', service_for(Board(image_items()))):
            await session._publish_device_pasteboard()
        self.assertEqual([], self.images(session))
        self.assertTrue(any(e.get('code') == 'clipboard_image_too_large' for e in session.ipc.events))

    def test_host_image_frames_are_validated(self):
        ok = {'uti': 'public.png', 'data': base64.b64encode(PNG).decode()}
        self.assertEqual(('public.png', PNG), bridge._decode_host_clipboard_image(ok))
        for bad in ({'uti': 'public.gif', 'data': ok['data']},
                    {'uti': 'public.png', 'data': 'not base64!'},
                    {'uti': 'public.png', 'data': ''},
                    {'uti': 'public.png'}):
            with self.assertRaises(Exception):
                bridge._decode_host_clipboard_image(bad)
        big = {'uti': 'public.png',
               'data': base64.b64encode(b'x' * (bridge.MAX_HOST_CLIPBOARD_IMAGE_BYTES + 1)).decode()}
        with self.assertRaises(ValueError):
            bridge._decode_host_clipboard_image(big)

    def test_capability_is_advertised(self):
        self.assertIn('iphoneMirror.clipboard.image.v1', bridge.CAPABILITIES)


if __name__ == '__main__':
    unittest.main()
