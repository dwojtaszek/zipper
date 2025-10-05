#!/bin/bash

# Fetch all tags from the remote
git fetch --all --tags

# Get all tags
tags=$(git tag)

# Ask the user for a regex
read -p "Enter a regex to match tags to delete (e.g., 'v0.17.*'): " regex

# Find tags that match the regex
tags_to_delete=$(echo "$tags" | grep -E "$regex")

if [ -z "$tags_to_delete" ]; then
  echo "No tags found matching the regex."
  exit 0
fi

echo "The following tags will be deleted:"
echo "$tags_to_delete"

read -p "Are you sure you want to delete these tags? (y/n): " confirm

if [ "$confirm" != "y" ]; then
  echo "Aborting."
  exit 0
fi

# Delete the tags
for tag in $tags_to_delete; do
  git tag -d "$tag"
  git push origin --delete "$tag"
done

echo "Tags deleted successfully."
