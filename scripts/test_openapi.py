"""Unit tests for the pure helpers in openapi.py.

Run from the repository root:  python -m unittest discover -s scripts -p "test_*.py"
"""

import unittest

from openapi import sorted_keys


class SortedKeysTests(unittest.TestCase):
    def test_given_an_object_when_sorted_then_its_keys_are_in_order_at_every_level(self):
        document = {"paths": {}, "info": {"version": "v1", "title": "Fortuna"}, "openapi": "3.1.2"}

        normalized = sorted_keys(document)

        self.assertEqual(["info", "openapi", "paths"], list(normalized))
        self.assertEqual(["title", "version"], list(normalized["info"]))

    def test_given_a_media_type_map_when_sorted_then_its_order_is_kept(self):
        body = {"content": {"application/json": {}, "text/json": {}, "application/*+json": {}}, "required": True}

        normalized = sorted_keys({"requestBody": body})

        self.assertEqual(
            ["application/json", "text/json", "application/*+json"],
            list(normalized["requestBody"]["content"]),
        )

    def test_given_a_media_type_when_sorted_then_its_own_keys_are_sorted(self):
        content = {"content": {"application/json": {"schema": {"type": "string", "format": "decimal"}}}}

        normalized = sorted_keys(content)

        self.assertEqual(["format", "type"], list(normalized["content"]["application/json"]["schema"]))

    def test_given_a_property_called_content_when_sorted_then_its_schema_is_sorted(self):
        schema = {"properties": {"content": {"type": "string", "format": "byte"}}}

        normalized = sorted_keys(schema)

        self.assertEqual(["format", "type"], list(normalized["properties"]["content"]))

    def test_given_a_list_when_sorted_then_its_order_is_kept(self):
        schema = {"type": ["null", "string"], "required": ["b", "a"]}

        normalized = sorted_keys(schema)

        self.assertEqual({"required": ["b", "a"], "type": ["null", "string"]}, normalized)
        self.assertEqual(["required", "type"], list(normalized))


if __name__ == "__main__":
    unittest.main()
