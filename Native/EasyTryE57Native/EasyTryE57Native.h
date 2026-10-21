#pragma once

#include <cstddef>
#include <cstdint>

#ifdef _WIN32
#  ifdef EASYTRY_E57_NATIVE_EXPORTS
#    define EASYTRY_E57_API __declspec(dllexport)
#  else
#    define EASYTRY_E57_API __declspec(dllimport)
#  endif
#  define EASYTRY_E57_CALL __cdecl
#else
#  define EASYTRY_E57_API
#  define EASYTRY_E57_CALL
#endif

extern "C"
{
   EASYTRY_E57_API void *EASYTRY_E57_CALL easytry_e57_create(
      const char *file_path_utf8,
      const char *file_guid_utf8,
      const char *scan_guid_utf8,
      const char *scan_name_utf8,
      const char *description_utf8,
      std::uint32_t scan_rows,
      std::uint32_t scan_columns,
      std::size_t buffer_capacity );

   EASYTRY_E57_API int EASYTRY_E57_CALL easytry_e57_write(
      void *handle,
      const double *x,
      const double *y,
      const double *z,
      const float *processed_intensity,
      const double *raw_intensity,
      const std::uint32_t *row_index,
      const std::uint32_t *column_index,
      std::size_t point_count );

   EASYTRY_E57_API int EASYTRY_E57_CALL easytry_e57_complete( void *handle );
   EASYTRY_E57_API void EASYTRY_E57_CALL easytry_e57_cancel( void *handle );
   EASYTRY_E57_API void EASYTRY_E57_CALL easytry_e57_destroy( void *handle );

   EASYTRY_E57_API int EASYTRY_E57_CALL easytry_e57_validate(
      const char *file_path_utf8,
      int read_all_points,
      std::uint64_t *point_count,
      std::uint32_t *field_mask );

   EASYTRY_E57_API void *EASYTRY_E57_CALL easytry_e57_reader_open(
      const char *file_path_utf8,
      std::size_t buffer_capacity,
      std::uint32_t *scan_rows,
      std::uint32_t *scan_columns,
      std::uint64_t *point_count,
      std::uint32_t *field_mask );

   EASYTRY_E57_API int EASYTRY_E57_CALL easytry_e57_reader_read(
      void *handle,
      double *x,
      double *y,
      double *z,
      float *processed_intensity,
      double *raw_intensity,
      std::uint32_t *row_index,
      std::uint32_t *column_index,
      std::size_t destination_capacity,
      std::size_t *points_read );

   EASYTRY_E57_API void EASYTRY_E57_CALL easytry_e57_reader_destroy( void *handle );

   EASYTRY_E57_API const char *EASYTRY_E57_CALL easytry_e57_last_error();
}
