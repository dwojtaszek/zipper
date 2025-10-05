#!/bin/bash

# Get all workflow runs
gh run list

# Ask the user for a status to filter by
read -p "Enter a status to filter by (e.g., completed, failed, cancelled): " status

# Ask for a date to delete runs older than that
read -p "Enter a date to delete runs older than (YYYY-MM-DD): " date

# Get the runs to delete
runs_to_delete=$(gh run list --status "$status" --created "<$date" --json databaseId -q 'map(.databaseId) | .[]')

if [ -z "$runs_to_delete" ]; then
  echo "No runs found matching the criteria."
  exit 0
fi

echo "The following runs will be deleted:"
gh run list --status "$status" --created "<$date"

read -p "Are you sure you want to delete these runs? (y/n): " confirm

if [ "$confirm" != "y" ]; then
  echo "Aborting."
  exit 0
fi

# Delete the runs
for run_id in $runs_to_delete; do
  gh run delete "$run_id"
done

echo "Runs deleted successfully."
